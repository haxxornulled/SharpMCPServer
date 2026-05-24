using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Application.WorkItems;

public readonly record struct AgentRunWorkItem(
    AgentRunId RunId,
    AgentObjective Objective,
    IReadOnlyDictionary<string, string?>? Metadata,
    DateTimeOffset EnqueuedAtUtc)
{
    public IReadOnlyDictionary<string, string?> MetadataOrEmpty => Metadata ?? AgentRouterMetadata.Empty;

    public bool IsValid => !RunId.IsEmpty && !Objective.IsEmpty;
}
