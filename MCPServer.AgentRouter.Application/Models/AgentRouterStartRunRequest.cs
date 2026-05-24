using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Domain.Objectives;

namespace MCPServer.AgentRouter.Application.Models;

public readonly record struct AgentRouterStartRunRequest(
    AgentObjective Objective,
    IReadOnlyDictionary<string, string?>? Metadata)
{
    public IReadOnlyDictionary<string, string?> MetadataOrEmpty => Metadata ?? AgentRouterMetadata.Empty;
}
