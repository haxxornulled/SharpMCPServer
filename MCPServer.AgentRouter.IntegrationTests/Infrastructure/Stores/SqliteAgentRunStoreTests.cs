using Autofac;
using LanguageExt;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Infrastructure.Options;
using MCPServer.AgentRouter.Infrastructure.Stores;
using Xunit;

namespace MCPServer.AgentRouter.Infrastructure.Tests.Stores;

public sealed class SqliteAgentRunStoreTests
{
    [Fact]
    public void AgentRouterInfrastructureModule_Resolves_Sqlite_Run_Store_By_Default()
    {
        using var container = BuildContainer();

        var store = container.Resolve<IAgentRunStore>();

        Assert.IsType<SqliteAgentRunStore>(store);
    }

    [Fact]
    public async Task TryCreateAsync_Persists_And_Loads_Snapshot()
    {
        using var container = BuildContainer();
        var store = container.Resolve<IAgentRunStore>();
        var snapshot = CreateSnapshot(version: 0, metadata: new Dictionary<string, string?>
        {
            ["agent.capability"] = "test-capability",
            ["nullable"] = null
        });

        var create = await store.TryCreateAsync(snapshot, TestContext.Current.CancellationToken);
        var loaded = await store.GetSnapshotAsync(snapshot.RunId, TestContext.Current.CancellationToken);

        Assert.True(IsSuccess(create), FailureMessage(create));
        var loadedSnapshot = UnsafeValue(loaded);
        Assert.Equal(snapshot.RunId, loadedSnapshot.RunId);
        Assert.Equal(snapshot.Objective, loadedSnapshot.Objective);
        Assert.Equal(snapshot.Status, loadedSnapshot.Status);
        Assert.Equal(0, loadedSnapshot.Version);
        Assert.Equal("test-capability", loadedSnapshot.MetadataOrEmpty["agent.capability"]);
        Assert.True(loadedSnapshot.MetadataOrEmpty.ContainsKey("nullable"));
    }

    [Fact]
    public async Task TryUpdateAsync_Accepts_Matching_Expected_Version()
    {
        using var container = BuildContainer();
        var store = container.Resolve<IAgentRunStore>();
        var initial = CreateSnapshot(version: 0);
        var updated = initial with
        {
            Status = AgentRunStatuses.Planning,
            Version = 1,
            UpdatedAtUtc = initial.UpdatedAtUtc.AddSeconds(1),
            Message = "planning"
        };

        var createInitial = await store.TryCreateAsync(initial, TestContext.Current.CancellationToken);
        Assert.True(IsSuccess(createInitial), FailureMessage(createInitial));
        var update = await store.TryUpdateAsync(updated, expectedVersion: 0, cancellationToken: TestContext.Current.CancellationToken);
        var loaded = await store.GetSnapshotAsync(initial.RunId, TestContext.Current.CancellationToken);

        Assert.True(IsSuccess(update), FailureMessage(update));
        var loadedSnapshot = UnsafeValue(loaded);
        Assert.Equal(AgentRunStatuses.Planning, loadedSnapshot.Status);
        Assert.Equal(1, loadedSnapshot.Version);
    }

    [Fact]
    public async Task TryUpdateAsync_Rejects_Stale_Expected_Version()
    {
        using var container = BuildContainer();
        var store = container.Resolve<IAgentRunStore>();
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

        var createInitial = await store.TryCreateAsync(initial, TestContext.Current.CancellationToken);
        Assert.True(IsSuccess(createInitial), FailureMessage(createInitial));
        var firstUpdateResult = await store.TryUpdateAsync(firstUpdate, expectedVersion: 0, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(IsSuccess(firstUpdateResult), FailureMessage(firstUpdateResult));

        var stale = await store.TryUpdateAsync(staleUpdate, expectedVersion: 0, cancellationToken: TestContext.Current.CancellationToken);
        var loaded = await store.GetSnapshotAsync(initial.RunId, TestContext.Current.CancellationToken);

        Assert.True(IsFailure(stale));
        var loadedSnapshot = UnsafeValue(loaded);
        Assert.Equal(AgentRunStatuses.Planning, loadedSnapshot.Status);
        Assert.Equal(1, loadedSnapshot.Version);
    }

    [Fact]
    public async Task TryCreateAsync_Rejects_Duplicate_Run_Id()
    {
        using var container = BuildContainer();
        var store = container.Resolve<IAgentRunStore>();
        var snapshot = CreateSnapshot(version: 0);

        var first = await store.TryCreateAsync(snapshot, TestContext.Current.CancellationToken);
        var second = await store.TryCreateAsync(snapshot, TestContext.Current.CancellationToken);

        Assert.True(IsSuccess(first), FailureMessage(first));
        Assert.True(IsFailure(second));
    }


    [Fact]
    public async Task TryUpdateAsync_Persists_Execution_Lease()
    {
        using var container = BuildContainer();
        var store = container.Resolve<IAgentRunStore>();
        var initial = CreateSnapshot(version: 0);
        var leaseAcquiredAtUtc = initial.UpdatedAtUtc.AddSeconds(1);
        var leased = initial with
        {
            Version = 1,
            UpdatedAtUtc = leaseAcquiredAtUtc,
            Message = "leased",
            ExecutionLease = AgentExecutionLease.Acquire("sqlite-worker-1", leaseAcquiredAtUtc, TimeSpan.FromMinutes(5))
        };

        var create = await store.TryCreateAsync(initial, TestContext.Current.CancellationToken);
        Assert.True(IsSuccess(create), FailureMessage(create));
        var update = await store.TryUpdateAsync(leased, expectedVersion: 0, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(IsSuccess(update), FailureMessage(update));

        var loaded = UnsafeValue(await store.GetSnapshotAsync(initial.RunId, TestContext.Current.CancellationToken));

        Assert.True(loaded.HasExecutionLease);
        Assert.Equal("sqlite-worker-1", loaded.ExecutionLease?.OwnerId);
        Assert.Equal(leased.ExecutionLease?.AcquiredAtUtc, loaded.ExecutionLease?.AcquiredAtUtc);
        Assert.Equal(leased.ExecutionLease?.ExpiresAtUtc, loaded.ExecutionLease?.ExpiresAtUtc);
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(new AgentRouterSqliteOptions
        {
            ConnectionString = $"Data Source={Path.Combine(Path.GetTempPath(), $"agent-router-tests-{Guid.NewGuid():N}.db")};Cache=Shared"
        }).AsSelf().SingleInstance();
        builder.RegisterModule(new AgentRouterInfrastructureModule());
        return builder.Build();
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


    private static string FailureMessage<T>(Fin<T> result)
    {
        return result.Match(
            Succ: static _ => string.Empty,
            Fail: static error => error.Message);
    }

    private static AgentRunSnapshot CreateSnapshot(
        long version,
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentRunSnapshot(
            AgentRunId.New(),
            CreateObjective("persist agent run in sqlite"),
            AgentRunStatuses.Queued,
            now,
            now,
            "queued",
            version,
            metadata);
    }

    private static AgentObjective CreateObjective(string value)
    {
        Assert.True(AgentObjective.TryCreate(value, out var objective));
        return objective;
    }
}
