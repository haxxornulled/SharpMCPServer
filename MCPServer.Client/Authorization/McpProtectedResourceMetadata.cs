using System.Text.Json.Serialization;

namespace MCPServer.Client.Authorization;

public sealed class McpProtectedResourceMetadata
{
    [JsonPropertyName("resource")]
    public string? Resource { get; init; }

    [JsonPropertyName("authorization_servers")]
    public string[] AuthorizationServers { get; init; } = Array.Empty<string>();

    [JsonPropertyName("scopes_supported")]
    public string[]? ScopesSupported { get; init; }
}
