using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure.Mcp.Http.Authorization;
using MCPServer.Infrastructure.Mcp.JsonRpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MCPServer.Infrastructure.Mcp.Http;

public sealed class StreamableHttpMcpServerService : BackgroundService
{
    private const string JsonContentType = "application/json";
    private const string TextEventStreamContentType = "text/event-stream";
    private const string AllowHeaderValue = "GET, POST, DELETE";

    private readonly StreamableHttpMcpTransportOptions _options;
    private readonly IMcpHttpAuthorizationService _authorizationService;
    private readonly IMcpProtectedResourceMetadataProvider _metadataProvider;
    private readonly IStreamableHttpMcpSessionManager _sessionManager;
    private readonly IStreamableHttpMcpRequestProcessor _processor;
    private readonly IJsonRpcResponseSerializer _serializer;
    private readonly ILogger<StreamableHttpMcpServerService> _logger;

    public StreamableHttpMcpServerService(
        StreamableHttpMcpTransportOptions options,
        IMcpHttpAuthorizationService authorizationService,
        IMcpProtectedResourceMetadataProvider metadataProvider,
        IStreamableHttpMcpSessionManager sessionManager,
        IStreamableHttpMcpRequestProcessor processor,
        IJsonRpcResponseSerializer serializer,
        ILogger<StreamableHttpMcpServerService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(metadataProvider);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _authorizationService = authorizationService;
        _metadataProvider = metadataProvider;
        _sessionManager = sessionManager;
        _processor = processor;
        _serializer = serializer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("MCP Streamable HTTP transport is disabled.");
            return;
        }

        try
        {
            await RunServerAsync(_options.Port, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (_options.Port == StreamableHttpMcpTransportOptions.DefaultLoopbackPort && IsAddressInUse(ex))
        {
            var fallbackPort = GetAvailableLoopbackPort();
            _logger.LogWarning(
                "MCP Streamable HTTP transport port {Port} was unavailable. Falling back to loopback port {FallbackPort}.",
                _options.Port,
                fallbackPort);

            await RunServerAsync(fallbackPort, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunServerAsync(int port, CancellationToken stoppingToken)
    {
        await using var app = CreateApplication(port, stoppingToken);
        await app.StartAsync(stoppingToken).ConfigureAwait(false);

        _logger.LogInformation(
            "MCP Streamable HTTP transport started on {Prefixes}",
            string.Join(", ", GetListeningUrls(port)));

        try
        {
            await app.WaitForShutdownAsync(stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private WebApplication CreateApplication(int port, CancellationToken stoppingToken)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(StreamableHttpMcpServerService).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(GetListeningUrls(port).ToArray());

        var app = builder.Build();
        app.Run(context => HandleContextAsync(context, stoppingToken));
        return app;
    }

    private IReadOnlyList<string> GetListeningUrls(int port)
    {
        var portText = port.ToString(CultureInfo.InvariantCulture);
        if (_options.BindLoopbackOnly)
        {
            return [$"http://127.0.0.1:{portText}"];
        }

        return [$"http://*:{portText}"];
    }

    private async Task HandleContextAsync(HttpContext context, CancellationToken stoppingToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stoppingToken);
        var cancellationToken = linkedCts.Token;

        try
        {
            var requestUri = new Uri(context.Request.GetDisplayUrl());

            if (_options.Authorization.Enabled && _metadataProvider.IsProtectedResourceMetadataRequest(requestUri))
            {
                await HandleProtectedResourceMetadataAsync(context, requestUri, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!IsMcpRequest(requestUri))
            {
                await WriteResponseAsync(context.Response, NotFound(), cancellationToken).ConfigureAwait(false);
                return;
            }

            var requestEnvelope = ReadRequestEnvelope(context.Request);
            if (_options.Authorization.Enabled)
            {
                var authorizationDecision = await _authorizationService.AuthorizeAsync(requestEnvelope, cancellationToken).ConfigureAwait(false);
                if (!authorizationDecision.IsAuthorized)
                {
                    await WriteAuthorizationFailureAsync(context.Response, authorizationDecision, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            var request = await ReadRequestAsync(context.Request, requestEnvelope, cancellationToken).ConfigureAwait(false);

            if (string.Equals(request.Method, HttpMethod.Get.Method, StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetAsync(context, request, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(request.Method, HttpMethod.Delete.Method, StringComparison.OrdinalIgnoreCase))
            {
                await HandleDeleteAsync(context, request, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(request.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase))
            {
                var response = await _processor.ProcessAsync(request, cancellationToken).ConfigureAwait(false);
                await WriteResponseAsync(context.Response, response, cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteResponseAsync(context.Response, MethodNotAllowed(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process MCP Streamable HTTP request.");
            await TryWriteFailureAsync(context.Response, HttpStatusCode.InternalServerError, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleProtectedResourceMetadataAsync(HttpContext context, Uri requestUri, CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Request.Method, HttpMethod.Get.Method, StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(context.Response, MethodNotAllowed("GET"), cancellationToken).ConfigureAwait(false);
            return;
        }

        var document = _metadataProvider.CreateDocument(requestUri);
        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            stream,
            document,
            McpHttpAuthorizationJsonSerializerContext.Default.McpProtectedResourceMetadataDocument,
            cancellationToken).ConfigureAwait(false);

        await WriteResponseAsync(
            context.Response,
            new StreamableHttpMcpResponse
            {
                StatusCode = HttpStatusCode.OK,
                ContentType = JsonContentType,
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Cache-Control"] = "no-cache"
                },
                Body = stream.ToArray()
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleGetAsync(HttpContext context, StreamableHttpMcpRequest request, CancellationToken cancellationToken)
    {
        if (!TryValidateOrigin(request, out var originError))
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(HttpStatusCode.Forbidden, originError).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryValidateGetHeaders(request, out var getHeaderError))
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(HttpStatusCode.BadRequest, getHeaderError).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_sessionManager.TryValidateSessionRequest(request, isInitialize: false, out var session, out var sessionStatus, out var sessionError))
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(sessionStatus, sessionError).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (session is null)
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(HttpStatusCode.NotFound, "Session not found.").ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryValidateProtocolVersion(request, out var protocolVersionError))
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(HttpStatusCode.BadRequest, protocolVersionError).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!session.Transport.TryValidateEventStreamRequest(request.GetHeader(StreamableHttpMcpHeaderNames.LastEventId), out var eventStreamStatus, out var eventStreamError))
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(eventStreamStatus, eventStreamError).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        var response = context.Response;
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = TextEventStreamContentType;
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";
        response.Headers[StreamableHttpMcpHeaderNames.SessionId] = session.SessionId;

        await response.StartAsync(cancellationToken).ConfigureAwait(false);
        await session.Transport.OpenEventStreamAsync(
            response.Body,
            request.GetHeader(StreamableHttpMcpHeaderNames.LastEventId),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleDeleteAsync(HttpContext context, StreamableHttpMcpRequest request, CancellationToken cancellationToken)
    {
        if (!TryValidateOrigin(request, out var originError))
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(HttpStatusCode.Forbidden, originError).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryValidateDeleteHeaders(request, out var deleteHeaderError))
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(HttpStatusCode.BadRequest, deleteHeaderError).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_sessionManager.TryValidateSessionRequest(request, isInitialize: false, out var session, out var sessionStatus, out var sessionError))
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(sessionStatus, sessionError).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (session is null)
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(HttpStatusCode.NotFound, "Session not found.").ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryValidateProtocolVersion(request, out var protocolVersionError))
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(HttpStatusCode.BadRequest, protocolVersionError).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_sessionManager.TryTerminateSession(session.SessionId))
        {
            await WriteResponseAsync(context.Response, await BuildErrorResponseAsync(HttpStatusCode.NotFound, "Session not found.").ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
        context.Response.ContentLength = 0;
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static StreamableHttpMcpRequestEnvelope ReadRequestEnvelope(HttpRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = header.Value.ToString();
        }

        return new StreamableHttpMcpRequestEnvelope
        {
            Method = request.Method,
            RequestUri = new Uri(request.GetDisplayUrl()),
            Headers = headers
        };
    }

    private static async ValueTask<StreamableHttpMcpRequest> ReadRequestAsync(HttpRequest request, StreamableHttpMcpRequestEnvelope requestEnvelope, CancellationToken cancellationToken)
    {
        await using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);

        return new StreamableHttpMcpRequest
        {
            Method = requestEnvelope.Method,
            RequestUri = requestEnvelope.RequestUri,
            Headers = requestEnvelope.Headers,
            Body = memory.ToArray()
        };
    }

    private async ValueTask WriteAuthorizationFailureAsync(HttpResponse response, McpHttpAuthorizationDecision decision, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)decision.StatusCode;
        if (!string.IsNullOrWhiteSpace(decision.WwwAuthenticate))
        {
            response.Headers["WWW-Authenticate"] = decision.WwwAuthenticate;
        }

        response.ContentLength = 0;
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteResponseAsync(HttpResponse response, StreamableHttpMcpResponse transportResponse, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)transportResponse.StatusCode;

        if (transportResponse.ContentType is { Length: > 0 })
        {
            response.ContentType = transportResponse.ContentType;
        }

        if (transportResponse.Headers is not null)
        {
            foreach (var header in transportResponse.Headers)
            {
                response.Headers[header.Key] = header.Value;
            }
        }

        var body = transportResponse.Body ?? Array.Empty<byte>();
        response.ContentLength = body.Length;
        if (body.Length == 0)
        {
            await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await response.Body.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask TryWriteFailureAsync(HttpResponse response, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        try
        {
            response.StatusCode = (int)statusCode;
            response.ContentLength = 0;
            await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static StreamableHttpMcpResponse MethodNotAllowed(string? allow = null)
    {
        return new StreamableHttpMcpResponse
        {
            StatusCode = HttpStatusCode.MethodNotAllowed,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Allow"] = allow ?? AllowHeaderValue
            }
        };
    }

    private static StreamableHttpMcpResponse NotFound()
    {
        return new StreamableHttpMcpResponse
        {
            StatusCode = HttpStatusCode.NotFound
        };
    }

    private async ValueTask<StreamableHttpMcpResponse> BuildErrorResponseAsync(HttpStatusCode statusCode, string message)
    {
        var response = JsonRpcResponse.Failure(JsonRpcRequestId.Missing, JsonRpcErrorCodes.InvalidRequest, message);
        await using var stream = new MemoryStream();
        await _serializer.WriteAsync(stream, response, CancellationToken.None).ConfigureAwait(false);

        return new StreamableHttpMcpResponse
        {
            StatusCode = statusCode,
            ContentType = JsonContentType,
            Body = stream.ToArray()
        };
    }

    private static bool TryValidateOrigin(StreamableHttpMcpRequest request, out string error)
    {
        var originHeader = request.GetHeader(StreamableHttpMcpHeaderNames.Origin);
        if (string.IsNullOrWhiteSpace(originHeader))
        {
            error = string.Empty;
            return true;
        }

        if (!Uri.TryCreate(originHeader, UriKind.Absolute, out var originUri) ||
            !originUri.IsLoopback ||
            !string.Equals(originUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(originUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Invalid Origin header.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateGetHeaders(StreamableHttpMcpRequest request, out string error)
    {
        if (!HasMediaType(request.GetHeader(StreamableHttpMcpHeaderNames.Accept), TextEventStreamContentType))
        {
            error = "Accept header must include text/event-stream.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateDeleteHeaders(StreamableHttpMcpRequest request, out string error)
    {
        error = string.Empty;
        return true;
    }

    private static bool TryValidateProtocolVersion(StreamableHttpMcpRequest request, out string error)
    {
        var protocolVersion = request.GetHeader(StreamableHttpMcpHeaderNames.ProtocolVersion);
        if (string.IsNullOrWhiteSpace(protocolVersion))
        {
            error = "Missing MCP-Protocol-Version header.";
            return false;
        }

        if (!McpProtocolVersions.IsSupported(protocolVersion))
        {
            error = $"Unsupported MCP protocol version '{protocolVersion}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool IsMcpRequest(Uri requestUri)
    {
        var configuredPath = StreamableHttpMcpTransportOptions.NormalizePath(_options.Path);
        var requestPath = requestUri.AbsolutePath;
        return string.Equals(requestPath, configuredPath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(requestPath.TrimEnd('/'), configuredPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasMediaType(string? headerValue, string mediaType)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var values = headerValue.Split(',');
        for (var i = 0; i < values.Length; i++)
        {
            var token = values[i].Trim();
            var semicolonIndex = token.IndexOf(';');
            if (semicolonIndex >= 0)
            {
                token = token[..semicolonIndex].Trim();
            }

            if (string.Equals(token, mediaType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAddressInUse(Exception ex)
    {
        return ex is AddressInUseException ||
               ex is SocketException socketException && IsAddressInUse(socketException) ||
               ex is IOException ioException && ioException.InnerException is SocketException innerSocketException && IsAddressInUse(innerSocketException);
    }

    private static bool IsAddressInUse(SocketException socketException)
    {
        return socketException.SocketErrorCode is SocketError.AddressAlreadyInUse or
            SocketError.AddressNotAvailable or
            SocketError.AccessDenied;
    }

    private static int GetAvailableLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
