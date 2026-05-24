using MCPServer.AgentRouter.Domain.Objectives;

namespace MCPServer.AgentRouter.Domain.Runs;

public readonly record struct AgentRunSnapshot(
    AgentRunId RunId,
    AgentObjective Objective,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? Message,
    long Version = 0,
    IReadOnlyDictionary<string, string?>? Metadata = null)
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyMetadata =
        new Dictionary<string, string?>(capacity: 0);

    public IReadOnlyDictionary<string, string?> MetadataOrEmpty => Metadata ?? EmptyMetadata;
}
