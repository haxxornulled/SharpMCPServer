using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Client.Authorization;
using MCPServer.Client.Interfaces;
using MCPServer.Client.Stdio;
using MCPServer.Client.StreamableHttp;
using MCPServer.Domain.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleSessionComposition
{
    public static async ValueTask<Fin<McpClientConsoleSessionScope>> CreateScopeAsync(
        ConsoleOptions options,
        IMcpAuthorizationProvider? authorizationProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        HttpClientFactoryScope? httpClientFactoryScope = null;
        try
        {
            if (options.Transport is ConsoleTransportKind.Http)
            {
                httpClientFactoryScope = HttpClientFactoryScope.Create();
            }

            var sessionResult = await CreateSessionAsync(
                options,
                httpClientFactoryScope?.Factory,
                authorizationProvider,
                cancellationToken).ConfigureAwait(false);

            return sessionResult.Match(
                Succ: session => Fin.Succ(new McpClientConsoleSessionScope(session, httpClientFactoryScope)),
                Fail: error =>
                {
                    httpClientFactoryScope?.Dispose();
                    return Fin.Fail<McpClientConsoleSessionScope>(error);
                });
        }
        catch (Exception ex)
        {
            httpClientFactoryScope?.Dispose();
            return Fin.Fail<McpClientConsoleSessionScope>(Error.New(ex.Message));
        }
    }

    private static async ValueTask<Fin<IMcpClientSession>> CreateSessionAsync(
        ConsoleOptions options,
        IHttpClientFactory? httpClientFactory,
        IMcpAuthorizationProvider? authorizationProvider,
        CancellationToken cancellationToken)
    {
        return options.Transport switch
        {
            ConsoleTransportKind.Stdio => await CreateStdioSessionAsync(options, cancellationToken).ConfigureAwait(false),
            ConsoleTransportKind.Http => await CreateHttpSessionAsync(options, httpClientFactory, authorizationProvider, cancellationToken).ConfigureAwait(false),
            _ => Fin.Fail<IMcpClientSession>(Error.New("Unsupported console transport."))
        };
    }

    private static async ValueTask<Fin<IMcpClientSession>> CreateStdioSessionAsync(ConsoleOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ServerPath))
        {
            return Fin.Fail<IMcpClientSession>(Error.New("--server-path is required for stdio mode."));
        }

        var processOptions = new McpClientProcessOptions
        {
            ServerExecutablePath = options.ServerPath,
            ServerArguments = options.ServerArguments,
            WorkingDirectory = options.WorkingDirectory,
            ClientName = "mcpserver-client-console",
            ClientTitle = "MCP Server Client Console",
            ClientVersion = "1.0.0"
        };

        var started = await StdioMcpClientSession.StartAsync(processOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        return started.Match(
            Succ: static session => Fin.Succ<IMcpClientSession>(session),
            Fail: static error => Fin.Fail<IMcpClientSession>(error));
    }

    private static async ValueTask<Fin<IMcpClientSession>> CreateHttpSessionAsync(
        ConsoleOptions options,
        IHttpClientFactory? httpClientFactory,
        IMcpAuthorizationProvider? authorizationProvider,
        CancellationToken cancellationToken)
    {
        if (options.Endpoint is not { } endpoint)
        {
            return Fin.Fail<IMcpClientSession>(Error.New("--endpoint is required for HTTP mode."));
        }

        if (httpClientFactory is null)
        {
            return Fin.Fail<IMcpClientSession>(Error.New("HTTP transport requires an IHttpClientFactory composition scope."));
        }

        var defaultHeaders = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(options.BearerToken))
        {
            var bearerToken = options.BearerToken.Trim();
            defaultHeaders["Authorization"] = "Bearer " + bearerToken;
            authorizationProvider = new StaticBearerTokenAuthorizationProvider(bearerToken);
        }

        var httpOptions = new McpStreamableHttpClientOptions
        {
            Endpoint = endpoint,
            ClientName = "mcpserver-client-console",
            ClientTitle = "MCP Server Client Console",
            ClientVersion = "1.0.0",
            HttpClientFactory = httpClientFactory,
            HttpClientName = "mcpserver-client-console-http",
            OpenServerEventStream = options.OpenServerEventStream,
            Origin = options.Origin ?? endpoint.GetLeftPart(UriPartial.Authority),
            DefaultHeaders = defaultHeaders,
            AuthorizationProvider = authorizationProvider
        };

        var started = await StreamableHttpMcpClientSession.StartAsync(httpOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        return started.Match(
            Succ: static session => Fin.Succ<IMcpClientSession>(session),
            Fail: static error => Fin.Fail<IMcpClientSession>(error));
    }
}

internal sealed class McpClientConsoleSessionScope : IAsyncDisposable, IDisposable
{
    private readonly HttpClientFactoryScope? _httpClientFactoryScope;
    private readonly IMcpClientSession _session;
    private bool _disposed;

    internal McpClientConsoleSessionScope(IMcpClientSession session, HttpClientFactoryScope? httpClientFactoryScope)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _httpClientFactoryScope = httpClientFactoryScope;
    }

    public IMcpClientSession Session => _session;

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        try
        {
            return DisposeAsyncCore();
        }
        catch
        {
            _httpClientFactoryScope?.Dispose();
            throw;
        }
    }

    private async ValueTask DisposeAsyncCore()
    {
        try
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _httpClientFactoryScope?.Dispose();
        }
    }
}

internal sealed class HttpClientFactoryScope : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    private HttpClientFactoryScope(ServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        Factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    }

    public IHttpClientFactory Factory { get; }

    public static HttpClientFactoryScope Create()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        var serviceProvider = services.BuildServiceProvider();
        return new HttpClientFactoryScope(serviceProvider);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}

internal sealed class StaticBearerTokenAuthorizationProvider : IMcpAuthorizationProvider
{
    private readonly string _bearerToken;

    public StaticBearerTokenAuthorizationProvider(string bearerToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        _bearerToken = bearerToken.Trim();
    }

    public ValueTask<Fin<string>> GetAccessTokenAsync(McpAuthorizationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Fin.Succ<string>(_bearerToken));
    }
}
