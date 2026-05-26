using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Infrastructure.Tracing;
using Xunit;

namespace MCPServer.AgentRouter.Infrastructure.Tests.Tracing;

public sealed class InMemoryAgentTraceWriterTests
{
    [Fact]
    public async Task WriteSnapshotAsync_Appends_Snapshot_To_InMemory_Trace()
    {
        var writer = new InMemoryAgentTraceWriter();
        var snapshot = CreateSnapshot();

        var result = await writer.WriteSnapshotAsync(snapshot, TestContext.Current.CancellationToken);

        Assert.True(result.Match(Succ: static _ => true, Fail: static _ => false));
        Assert.Single(writer.Snapshots);
        Assert.Contains(writer.Snapshots, value => value.RunId == snapshot.RunId && value.Version == snapshot.Version);
    }

    private static AgentRunSnapshot CreateSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentRunSnapshot(
            AgentRunId.New(),
            CreateObjective("append trace snapshot"),
            AgentRunStatuses.Queued,
            now,
            now,
            "queued",
            Version: 0);
    }

    private static AgentObjective CreateObjective(string value)
    {
        Assert.True(AgentObjective.TryCreate(value, out var objective));
        return objective;
    }
}
