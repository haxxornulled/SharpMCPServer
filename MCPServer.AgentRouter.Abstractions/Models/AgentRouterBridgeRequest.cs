using System.Text.Json.Serialization;
using MCPServer.AgentRouter.Abstractions;

namespace MCPServer.AgentRouter.Abstractions.Models;

public readonly record struct AgentRouterBridgeRequest(
    [property: JsonPropertyName("objective")] string? Objective,
    [property: JsonPropertyName("metadata")] Dictionary<string, string?>? Metadata)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, string?> MetadataOrEmpty => Metadata ?? AgentRouterMetadata.Empty;
}
