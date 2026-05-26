using System.Text.Json.Serialization;

namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public sealed class McpOpenIdConnectDiscoveryDocument
{
    [JsonPropertyName("issuer")]
    public string? Issuer { get; init; }

    [JsonPropertyName("jwks_uri")]
    public string? JwksUri { get; init; }
}
