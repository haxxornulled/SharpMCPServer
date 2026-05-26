using System.Text.Json.Serialization;

namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public sealed class McpProtectedResourceMetadataDocument
{
    [JsonPropertyName("resource")]
    public string Resource { get; init; } = string.Empty;

    [JsonPropertyName("authorization_servers")]
    public string[] AuthorizationServers { get; init; } = Array.Empty<string>();

    [JsonPropertyName("scopes_supported")]
    public string[]? ScopesSupported { get; init; }
}
