using System.Text.Json.Serialization;

namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(McpProtectedResourceMetadataDocument))]
[JsonSerializable(typeof(McpOpenIdConnectDiscoveryDocument))]
public sealed partial class McpHttpAuthorizationJsonSerializerContext : JsonSerializerContext
{
}
