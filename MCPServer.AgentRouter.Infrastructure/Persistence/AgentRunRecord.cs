using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Infrastructure.Persistence;

internal sealed class AgentRunRecord
{
    public string RunId { get; set; } = string.Empty;

    public string Objective { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? Message { get; set; }

    public long Version { get; set; }

    public string? MetadataJson { get; set; }

    public string? LeaseOwnerId { get; set; }

    public DateTimeOffset? LeaseAcquiredAtUtc { get; set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    public static AgentRunRecord FromSnapshot(AgentRunSnapshot snapshot)
    {
        return new AgentRunRecord
        {
            RunId = snapshot.RunId.Value,
            Objective = snapshot.Objective.Value,
            Status = snapshot.Status,
            CreatedAtUtc = snapshot.CreatedAtUtc,
            UpdatedAtUtc = snapshot.UpdatedAtUtc,
            Message = snapshot.Message,
            Version = snapshot.Version,
            MetadataJson = AgentRunSnapshotJson.SerializeMetadata(snapshot.Metadata),
            LeaseOwnerId = snapshot.ExecutionLease?.OwnerId,
            LeaseAcquiredAtUtc = snapshot.ExecutionLease?.AcquiredAtUtc,
            LeaseExpiresAtUtc = snapshot.ExecutionLease?.ExpiresAtUtc
        };
    }

    public AgentRunSnapshot ToSnapshot()
    {
        if (!AgentObjective.TryCreate(Objective, out var objective))
        {
            throw new InvalidOperationException($"Stored agent run '{RunId}' has an invalid objective.");
        }

        return new AgentRunSnapshot(
            new AgentRunId(RunId),
            objective,
            Status,
            CreatedAtUtc,
            UpdatedAtUtc,
            Message,
            Version,
            AgentRunSnapshotJson.DeserializeMetadata(MetadataJson),
            BuildExecutionLease());
    }

    private AgentExecutionLease? BuildExecutionLease()
    {
        return (LeaseOwnerId, LeaseAcquiredAtUtc, LeaseExpiresAtUtc) switch
        {
            ({ Length: > 0 } ownerId, { } acquiredAtUtc, { } expiresAtUtc) =>
                new AgentExecutionLease(ownerId, acquiredAtUtc, expiresAtUtc),
            _ => null
        };
    }
}
