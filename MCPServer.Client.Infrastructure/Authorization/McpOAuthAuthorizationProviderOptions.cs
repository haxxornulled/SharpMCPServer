namespace MCPServer.Client.Infrastructure.Authorization;

public sealed class McpOAuthAuthorizationProviderOptions
{
    public string ClientName { get; init; } = "MCP Server Client";

    public Uri? ClientUri { get; init; }

    public string? ClientId { get; init; }

    public Uri? ClientIdMetadataDocumentUri { get; init; }

    public Uri? RedirectUri { get; init; }

    public bool UseDynamicClientRegistration { get; init; } = true;

    public TimeSpan AuthorizationTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan TokenRequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan RefreshSkew { get; init; } = TimeSpan.FromMinutes(1);

    public string CallbackPath { get; init; } = "/oauth2/callback/";

    public string BrowserClientName { get; init; } = "MCP Server Client";

    public string HttpClientName { get; init; } = "mcpserver-oauth-authorization-client";
}
