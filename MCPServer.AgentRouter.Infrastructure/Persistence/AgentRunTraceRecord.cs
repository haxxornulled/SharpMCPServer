using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Infrastructure.Persistence;

internal sealed class AgentRunTraceRecord
{
    public long TraceId { get; set; }

    public string RunId { get; set; } = string.Empty;

    public string Objective { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? Message { get; set; }

    public long Version { get; set; }

    public string? MetadataJson { get; set; }

    public DateTimeOffset WrittenAtUtc { get; set; }

    public static AgentRunTraceRecord FromSnapshot(AgentRunSnapshot snapshot, DateTimeOffset writtenAtUtc)
    {
        return new AgentRunTraceRecord
        {
            RunId = snapshot.RunId.Value,
            Objective = snapshot.Objective.Value,
            Status = snapshot.Status,
            CreatedAtUtc = snapshot.CreatedAtUtc,
            UpdatedAtUtc = snapshot.UpdatedAtUtc,
            Message = snapshot.Message,
            Version = snapshot.Version,
            MetadataJson = AgentRunSnapshotJson.SerializeMetadata(snapshot.Metadata),
            WrittenAtUtc = writtenAtUtc
        };
    }
}
