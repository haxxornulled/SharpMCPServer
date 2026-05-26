using MCPServer.Client.Authorization;
using MCPServer.Client.Interfaces;
using Microsoft.Extensions.Http;

namespace MCPServer.Client.StreamableHttp;

public sealed class McpStreamableHttpClientOptions
{
    public Uri Endpoint { get; init; } = new Uri("http://localhost/", UriKind.Absolute);

    public string ClientName { get; init; } = "mcpserver-http-client";

    public string ClientTitle { get; init; } = "MCP Server HTTP Client";

    public string ClientVersion { get; init; } = "1.0.0";

    public bool SupportsSampling { get; init; }

    public bool SupportsSamplingTools { get; init; }

    public bool SupportsSamplingContext { get; init; }

    public bool SupportsElicitationForm { get; init; }

    public bool SupportsElicitationUrl { get; init; }

    public bool SupportsTasksList { get; init; }

    public bool SupportsTasksCancel { get; init; }

    public bool SupportsTaskSamplingCreateMessage { get; init; }

    public bool SupportsTaskElicitationCreate { get; init; }

    public IMcpClientSamplingHandler? SamplingRequestHandler { get; init; }

    public IMcpClientElicitationHandler? ElicitationRequestHandler { get; init; }

    public IMcpClientTaskRegistry? ClientTaskRegistry { get; init; }

    public IMcpTaskStatusObserver? TaskStatusObserver { get; init; }

    public IMcpAuthorizationProvider? AuthorizationProvider { get; init; }

    public IHttpClientFactory? HttpClientFactory { get; init; }

    public string HttpClientName { get; init; } = "mcpserver-streamable-http-client";

    public HttpMessageHandler? HttpMessageHandler { get; init; }

    public IReadOnlyDictionary<string, string?> DefaultHeaders { get; init; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public string? Origin { get; init; }

    public bool OpenServerEventStream { get; init; }

    public TimeSpan ServerEventReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);
}
