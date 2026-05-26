using System.Text.Json.Serialization;

namespace MCPServer.Client.Authorization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(McpAuthorizationServerMetadata))]
[JsonSerializable(typeof(McpOidcDiscoveryDocument))]
[JsonSerializable(typeof(McpProtectedResourceMetadata))]
[JsonSerializable(typeof(McpOAuthClientRegistrationRequest))]
[JsonSerializable(typeof(McpOAuthClientRegistrationResponse))]
[JsonSerializable(typeof(McpOAuthTokenResponse))]
public sealed partial class McpAuthorizationJsonSerializerContext : JsonSerializerContext
{
}
