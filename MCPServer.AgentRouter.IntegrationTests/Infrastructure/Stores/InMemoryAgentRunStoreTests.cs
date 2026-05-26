using LanguageExt;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Infrastructure.Stores;
using Xunit;

namespace MCPServer.AgentRouter.Infrastructure.Tests.Stores;

public sealed class InMemoryAgentRunStoreTests
{
    [Fact]
    public async Task TryCreateAsync_Persists_Initial_Version_Zero_Snapshot()
    {
        var store = new InMemoryAgentRunStore();
        var snapshot = CreateSnapshot(version: 0);

        var result = await store.TryCreateAsync(snapshot, TestContext.Current.CancellationToken);
        var loaded = await store.GetSnapshotAsync(snapshot.RunId, TestContext.Current.CancellationToken);

        Assert.True(IsSuccess(result));
        Assert.True(IsSuccess(loaded));
        Assert.Equal(0, UnsafeValue(loaded).Version);
    }

    [Fact]
    public async Task TryCreateAsync_Rejects_Duplicate_Run_Id()
    {
        var store = new InMemoryAgentRunStore();
        var snapshot = CreateSnapshot(version: 0);

        var first = await store.TryCreateAsync(snapshot, TestContext.Current.CancellationToken);
        var second = await store.TryCreateAsync(snapshot, TestContext.Current.CancellationToken);

        Assert.True(IsSuccess(first));
        Assert.True(IsFailure(second));
    }

    [Fact]
    public async Task TryUpdateAsync_Accepts_Matching_Expected_Version()
    {
        var store = new InMemoryAgentRunStore();
        var initial = CreateSnapshot(version: 0);
        var updated = initial with
        {
            Status = AgentRunStatuses.Planning,
            Version = 1,
            UpdatedAtUtc = initial.UpdatedAtUtc.AddSeconds(1),
            Message = "planning"
        };

        var create = await store.TryCreateAsync(initial, TestContext.Current.CancellationToken);
        var update = await store.TryUpdateAsync(updated, expectedVersion: 0, cancellationToken: TestContext.Current.CancellationToken);
        var loaded = await store.GetSnapshotAsync(initial.RunId, TestContext.Current.CancellationToken);

        Assert.True(IsSuccess(create));
        Assert.True(IsSuccess(update));
        var loadedSnapshot = UnsafeValue(loaded);
        Assert.Equal(AgentRunStatuses.Planning, loadedSnapshot.Status);
        Assert.Equal(1, loadedSnapshot.Version);
    }

    [Fact]
    public async Task TryUpdateAsync_Rejects_Stale_Expected_Version()
    {
        var store = new InMemoryAgentRunStore();
        var initial = CreateSnapshot(version: 0);
        var firstUpdate = initial with
        {
            Status = AgentRunStatuses.Planning,
            Version = 1,
            UpdatedAtUtc = initial.UpdatedAtUtc.AddSeconds(1),
            Message = "planning"
        };
        var staleUpdate = initial with
        {
            Status = AgentRunStatuses.Cancelled,
            Version = 1,
            UpdatedAtUtc = initial.UpdatedAtUtc.AddSeconds(2),
            Message = "stale cancel"
        };

        Assert.True(IsSuccess(await store.TryCreateAsync(initial, TestContext.Current.CancellationToken)));
        Assert.True(IsSuccess(await store.TryUpdateAsync(firstUpdate, expectedVersion: 0, cancellationToken: TestContext.Current.CancellationToken)));

        var stale = await store.TryUpdateAsync(staleUpdate, expectedVersion: 0, cancellationToken: TestContext.Current.CancellationToken);
        var loaded = await store.GetSnapshotAsync(initial.RunId, TestContext.Current.CancellationToken);

        Assert.True(IsFailure(stale));
        var loadedSnapshot = UnsafeValue(loaded);
        Assert.Equal(AgentRunStatuses.Planning, loadedSnapshot.Status);
        Assert.Equal(1, loadedSnapshot.Version);
    }


    [Fact]
    public async Task TryUpdateAsync_Persists_Execution_Lease()
    {
        var store = new InMemoryAgentRunStore();
        var initial = CreateSnapshot(version: 0);
        var leased = initial with
        {
            Version = 1,
            UpdatedAtUtc = initial.UpdatedAtUtc.AddSeconds(1),
            Message = "leased",
            ExecutionLease = AgentExecutionLease.Acquire("worker-1", initial.UpdatedAtUtc.AddSeconds(1), TimeSpan.FromMinutes(5))
        };

        Assert.True(IsSuccess(await store.TryCreateAsync(initial, TestContext.Current.CancellationToken)));
        Assert.True(IsSuccess(await store.TryUpdateAsync(leased, expectedVersion: 0, cancellationToken: TestContext.Current.CancellationToken)));

        var loaded = UnsafeValue(await store.GetSnapshotAsync(initial.RunId, TestContext.Current.CancellationToken));

        Assert.True(loaded.HasExecutionLease);
        Assert.Equal("worker-1", loaded.ExecutionLease?.OwnerId);
        Assert.Equal(leased.ExecutionLease?.ExpiresAtUtc, loaded.ExecutionLease?.ExpiresAtUtc);
    }

    private static bool IsSuccess<T>(Fin<T> result)
    {
        return result.Match(Succ: static _ => true, Fail: static _ => false);
    }

    private static bool IsFailure<T>(Fin<T> result)
    {
        return result.Match(Succ: static _ => false, Fail: static _ => true);
    }

    private static T UnsafeValue<T>(Fin<T> result)
    {
        return result.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));
    }

    private static AgentRunSnapshot CreateSnapshot(long version)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentRunSnapshot(
            AgentRunId.New(),
            CreateObjective("validate optimistic concurrency"),
            AgentRunStatuses.Queued,
            now,
            now,
            "queued",
            version);
    }

    private static AgentObjective CreateObjective(string value)
    {
        Assert.True(AgentObjective.TryCreate(value, out var objective));
        return objective;
    }
}
