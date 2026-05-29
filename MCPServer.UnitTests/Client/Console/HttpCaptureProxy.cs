using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace MCPServer.UnitTests.Client.Console;

internal sealed class HttpCaptureProxy : IAsyncDisposable
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade"
    };

    private readonly WebApplication _app;
    private readonly HttpClient _httpClient;
    private readonly Uri _upstreamBaseUri;
    private readonly ConcurrentQueue<CapturedHttpExchange> _exchanges = new();
    private int _sequence;

    private HttpCaptureProxy(WebApplication app, HttpClient httpClient, Uri baseUri, Uri upstreamBaseUri)
    {
        _app = app;
        _httpClient = httpClient;
        BaseUri = baseUri;
        _upstreamBaseUri = upstreamBaseUri;
    }

    public Uri BaseUri { get; }

    public IReadOnlyList<CapturedHttpExchange> Exchanges => _exchanges.OrderBy(exchange => exchange.Sequence).ToArray();

    public static async Task<HttpCaptureProxy> StartAsync(Uri upstreamBaseUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upstreamBaseUri);

        var port = GetAvailablePort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();

        var httpClient = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = 32,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        var proxy = new HttpCaptureProxy(
            app,
            httpClient,
            new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute),
            NormalizeBaseUri(upstreamBaseUri));

        app.Run(proxy.HandleAsync);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        return proxy;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        _httpClient.Dispose();

        try
        {
            await _app.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task HandleAsync(HttpContext context)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var requestBody = await ReadRequestBodyAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
        var requestHeaders = CaptureHeaders(context.Request.Headers);

        try
        {
            using var upstreamRequest = CreateUpstreamRequest(context, requestBody);
            using var upstreamResponse = await _httpClient.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted).ConfigureAwait(false);

            var responseHeaders = CaptureHeaders(upstreamResponse);
            var responseBody = await ForwardResponseAsync(context, upstreamResponse, context.RequestAborted).ConfigureAwait(false);
            _exchanges.Enqueue(new CapturedHttpExchange(
                sequence,
                context.Request.Method,
                BuildPathAndQuery(context),
                requestHeaders,
                Encoding.UTF8.GetString(requestBody),
                (int)upstreamResponse.StatusCode,
                responseHeaders,
                Encoding.UTF8.GetString(responseBody)));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(ex.Message, context.RequestAborted).ConfigureAwait(false);
            }
            catch
            {
            }

            _exchanges.Enqueue(new CapturedHttpExchange(
                sequence,
                context.Request.Method,
                BuildPathAndQuery(context),
                requestHeaders,
                Encoding.UTF8.GetString(requestBody),
                (int)HttpStatusCode.BadGateway,
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
                ex.Message));
        }
    }

    private HttpRequestMessage CreateUpstreamRequest(HttpContext context, byte[] requestBody)
    {
        var upstreamUri = new Uri(_upstreamBaseUri, context.Request.Path + context.Request.QueryString);
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), upstreamUri);

        foreach (var header in context.Request.Headers)
        {
            if (ShouldSkipRequestHeader(header.Key))
            {
                continue;
            }

            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        if (ShouldIncludeBody(context.Request.Method))
        {
            request.Content = new ByteArrayContent(requestBody);
            if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
            {
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
            }
        }

        return request;
    }

    private static async Task<byte[]> ReadRequestBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        await using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static async Task<byte[]> ForwardResponseAsync(HttpContext context, HttpResponseMessage upstreamResponse, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = (int)upstreamResponse.StatusCode;
        CopyResponseHeaders(upstreamResponse, context.Response);

        await context.Response.StartAsync(cancellationToken).ConfigureAwait(false);

        await using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var capture = new MemoryStream();
        var buffer = new byte[16 * 1024];

        while (true)
        {
            var read = await upstreamStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            await capture.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return capture.ToArray();
    }

    private static void CopyResponseHeaders(HttpResponseMessage upstreamResponse, HttpResponse downstreamResponse)
    {
        if (upstreamResponse.Content.Headers.ContentType is { } contentType)
        {
            downstreamResponse.ContentType = contentType.ToString();
        }

        foreach (var header in upstreamResponse.Headers)
        {
            if (ShouldSkipRequestHeader(header.Key))
            {
                continue;
            }

            downstreamResponse.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            if (ShouldSkipRequestHeader(header.Key) ||
                string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            downstreamResponse.Headers[header.Key] = header.Value.ToArray();
        }
    }

    private static Dictionary<string, string[]> CaptureHeaders(IHeaderDictionary headers)
    {
        var captured = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            captured[header.Key] = header.Value.Select(value => value ?? string.Empty).ToArray();
        }

        return captured;
    }

    private static Dictionary<string, string[]> CaptureHeaders(HttpResponseMessage response)
    {
        var captured = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            captured[header.Key] = header.Value.Select(value => value ?? string.Empty).ToArray();
        }

        foreach (var header in response.Content.Headers)
        {
            captured[header.Key] = header.Value.Select(value => value ?? string.Empty).ToArray();
        }

        return captured;
    }

    private static string BuildPathAndQuery(HttpContext context)
    {
        return string.Concat(context.Request.Path.Value ?? string.Empty, context.Request.QueryString.Value ?? string.Empty);
    }

    private static bool ShouldSkipRequestHeader(string headerName)
    {
        return string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase) || HopByHopHeaders.Contains(headerName);
    }

    private static bool ShouldIncludeBody(string method)
    {
        return !string.Equals(method, HttpMethod.Get.Method, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(method, HttpMethod.Delete.Method, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(method, HttpMethod.Head.Method, StringComparison.OrdinalIgnoreCase);
    }

    private static Uri NormalizeBaseUri(Uri baseUri)
    {
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The upstream base URI must be absolute.", nameof(baseUri));
        }

        var normalized = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri.AbsoluteUri
            : baseUri.AbsoluteUri + "/";

        return new Uri(normalized, UriKind.Absolute);
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

internal sealed record CapturedHttpExchange(
    int Sequence,
    string Method,
    string PathAndQuery,
    IReadOnlyDictionary<string, string[]> RequestHeaders,
    string RequestBody,
    int StatusCode,
    IReadOnlyDictionary<string, string[]> ResponseHeaders,
    string ResponseBody)
{
    public string? GetRequestHeader(string name)
    {
        return TryGetHeader(RequestHeaders, name);
    }

    public string? GetResponseHeader(string name)
    {
        return TryGetHeader(ResponseHeaders, name);
    }

    private static string? TryGetHeader(IReadOnlyDictionary<string, string[]> headers, string name)
    {
        return headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
    }
}
