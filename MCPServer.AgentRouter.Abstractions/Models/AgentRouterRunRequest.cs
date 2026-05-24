using MCPServer.AgentRouter.Abstractions;

namespace MCPServer.AgentRouter.Abstractions.Models;

public readonly record struct AgentRouterRunRequest(
    string? Objective,
    IReadOnlyDictionary<string, string?>? Metadata)
{
    public IReadOnlyDictionary<string, string?> MetadataOrEmpty => Metadata ?? AgentRouterMetadata.Empty;
}
