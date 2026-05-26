using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using Xunit;

namespace MCPServer.AgentRouter.Domain.Tests.Runs;

public sealed class AgentRunTests
{
    [Fact]
    public void Queue_Creates_Queued_Run_With_Version_Zero()
    {
        var now = DateTimeOffset.UtcNow;
        var run = AgentRun.Queue(AgentRunId.New(), CreateObjective("validate remote SSH user"), now, "queued");

        var snapshot = run.ToSnapshot();

        Assert.Equal(AgentRunStatuses.Queued, snapshot.Status);
        Assert.Equal(0, snapshot.Version);
        Assert.Equal(now, snapshot.CreatedAtUtc);
        Assert.Equal(now, snapshot.UpdatedAtUtc);
        Assert.False(snapshot.RunId.IsEmpty);
    }

    [Fact]
    public void Valid_Transition_Increments_Version_And_Updates_Message()
    {
        var now = DateTimeOffset.UtcNow;
        var run = AgentRun.Queue(AgentRunId.New(), CreateObjective("execute deterministic SSH workflow"), now, "queued");

        var result = run.MarkPlanning(now.AddSeconds(1), "planning");
        var snapshot = run.ToSnapshot();

        Assert.True(result.Succeeded);
        Assert.Equal(AgentRunStatuses.Planning, snapshot.Status);
        Assert.Equal(1, snapshot.Version);
        Assert.Equal("planning", snapshot.Message);
    }

    [Fact]
    public void Invalid_Transition_Is_Rejected_And_Does_Not_Increment_Version()
    {
        var now = DateTimeOffset.UtcNow;
        var run = AgentRun.Queue(AgentRunId.New(), CreateObjective("complete too early"), now, "queued");

        var result = run.Complete(now.AddSeconds(1), "complete");
        var snapshot = run.ToSnapshot();

        Assert.False(result.Succeeded);
        Assert.Equal(AgentRunTransitionErrorCodes.InvalidTransition, result.ErrorCode);
        Assert.Equal(AgentRunStatuses.Queued, snapshot.Status);
        Assert.Equal(0, snapshot.Version);
    }

    [Fact]
    public void Terminal_Run_Cannot_Transition_Again()
    {
        var now = DateTimeOffset.UtcNow;
        var run = AgentRun.Queue(AgentRunId.New(), CreateObjective("cancel once"), now, "queued");

        var first = run.Cancel(now.AddSeconds(1), "cancelled");
        var second = run.Fail(now.AddSeconds(2), "failed after cancel");
        var snapshot = run.ToSnapshot();

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(AgentRunTransitionErrorCodes.TerminalState, second.ErrorCode);
        Assert.Equal(AgentRunStatuses.Cancelled, snapshot.Status);
        Assert.Equal(1, snapshot.Version);
    }

    [Fact]
    public void Rehydrate_Restores_Snapshot_For_Domain_Behavior()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new AgentRunSnapshot(
            AgentRunId.New(),
            CreateObjective("resume from persisted snapshot"),
            AgentRunStatuses.Planning,
            now,
            now.AddSeconds(1),
            "planning",
            Version: 3);

        var run = AgentRun.Rehydrate(in snapshot);
        var result = run.MarkWorking(now.AddSeconds(2), "working");
        var next = run.ToSnapshot();

        Assert.True(result.Succeeded);
        Assert.Equal(AgentRunStatuses.Working, next.Status);
        Assert.Equal(4, next.Version);
    }


    [Fact]
    public void Approve_Moves_Awaiting_Approval_Run_Back_To_Queued_And_Preserves_Metadata()
    {
        var now = DateTimeOffset.UtcNow;
        var run = AgentRun.Queue(
            AgentRunId.New(),
            CreateObjective("approve critical remote shell"),
            now,
            "queued",
            new Dictionary<string, string?>
            {
                ["agent.capability"] = "remote-shell"
            });

        Assert.True(run.MarkPlanning(now.AddSeconds(1), "planning").Succeeded);
        Assert.True(run.MarkAwaitingApproval(now.AddSeconds(2), "requires approval").Succeeded);

        var approval = run.Approve(
            now.AddSeconds(3),
            "approval-domain-test-1",
            new Dictionary<string, string?>
            {
                ["agent.approval.approvedBy"] = "domain-test"
            });
        var snapshot = run.ToSnapshot();

        Assert.True(approval.Succeeded);
        Assert.Equal(AgentRunStatuses.Queued, snapshot.Status);
        Assert.Equal(3, snapshot.Version);
        Assert.Equal("remote-shell", snapshot.MetadataOrEmpty["agent.capability"]);
        Assert.Equal("true", snapshot.MetadataOrEmpty["agent.approval.granted"]);
        Assert.Equal("approval-domain-test-1", snapshot.MetadataOrEmpty["agent.approval.id"]);
        Assert.Equal("domain-test", snapshot.MetadataOrEmpty["agent.approval.approvedBy"]);
    }


    [Fact]
    public void AcquireExecutionLease_Adds_Lease_And_Increments_Version()
    {
        var now = DateTimeOffset.UtcNow;
        var run = AgentRun.Queue(AgentRunId.New(), CreateObjective("lease queued run"), now, "queued");

        var result = run.AcquireExecutionLease("worker-1", now.AddSeconds(1), TimeSpan.FromMinutes(5), "leased");
        var snapshot = run.ToSnapshot();

        Assert.True(result.Succeeded);
        Assert.Equal(AgentRunStatuses.Queued, snapshot.Status);
        Assert.Equal(1, snapshot.Version);
        Assert.True(snapshot.HasExecutionLease);
        Assert.Equal("worker-1", snapshot.ExecutionLease?.OwnerId);
        Assert.Equal(now.AddSeconds(1), snapshot.ExecutionLease?.AcquiredAtUtc);
        Assert.Equal(now.AddMinutes(5).AddSeconds(1), snapshot.ExecutionLease?.ExpiresAtUtc);
    }

    [Fact]
    public void AcquireExecutionLease_Rejects_Different_Worker_When_Unexpired_Lease_Exists()
    {
        var now = DateTimeOffset.UtcNow;
        var run = AgentRun.Queue(AgentRunId.New(), CreateObjective("reject second worker lease"), now, "queued");

        var first = run.AcquireExecutionLease("worker-1", now.AddSeconds(1), TimeSpan.FromMinutes(5), "leased");
        var second = run.AcquireExecutionLease("worker-2", now.AddSeconds(2), TimeSpan.FromMinutes(5), "leased by second");
        var snapshot = run.ToSnapshot();

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(AgentRunTransitionErrorCodes.LeaseAlreadyHeld, second.ErrorCode);
        Assert.Equal(1, snapshot.Version);
        Assert.Equal("worker-1", snapshot.ExecutionLease?.OwnerId);
    }

    [Fact]
    public void AcquireExecutionLease_Allows_Different_Worker_After_Lease_Expires()
    {
        var now = DateTimeOffset.UtcNow;
        var run = AgentRun.Queue(AgentRunId.New(), CreateObjective("expired lease can move"), now, "queued");

        var first = run.AcquireExecutionLease("worker-1", now.AddSeconds(1), TimeSpan.FromSeconds(1), "leased");
        var second = run.AcquireExecutionLease("worker-2", now.AddSeconds(3), TimeSpan.FromMinutes(5), "leased by second");
        var snapshot = run.ToSnapshot();

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal("worker-2", snapshot.ExecutionLease?.OwnerId);
    }

    [Fact]
    public void Terminal_Transition_Clears_Execution_Lease()
    {
        var now = DateTimeOffset.UtcNow;
        var run = AgentRun.Queue(AgentRunId.New(), CreateObjective("clear lease on complete"), now, "queued");

        Assert.True(run.AcquireExecutionLease("worker-1", now.AddSeconds(1), TimeSpan.FromMinutes(5), "leased").Succeeded);
        Assert.True(run.MarkPlanning(now.AddSeconds(2), "planning").Succeeded);
        Assert.True(run.MarkWorking(now.AddSeconds(3), "working").Succeeded);
        Assert.True(run.Complete(now.AddSeconds(4), "complete").Succeeded);
        var snapshot = run.ToSnapshot();

        Assert.Equal(AgentRunStatuses.Completed, snapshot.Status);
        Assert.False(snapshot.HasExecutionLease);
        Assert.Null(snapshot.ExecutionLease);
    }

    private static AgentObjective CreateObjective(string value)
    {
        Assert.True(AgentObjective.TryCreate(value, out var objective));
        return objective;
    }
}
