using System.Collections.Concurrent;
using Autofac;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.Execution.Abstractions.Models;
using MCPServer.AgentRouter.Application.WorkItems;
using MCPServer.AgentRouter.Application.Tests.Testing;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using Xunit;

namespace MCPServer.AgentRouter.Application.Tests.Services;

public sealed class DefaultAgentRunCoordinatorTests
{
    [Fact]
    public void AgentRouterApplicationModule_Resolves_Run_Coordinator_When_Ports_Are_Registered()
    {
        using var container = BuildContainer();

        var coordinator = container.Resolve<IAgentRunCoordinator>();

        Assert.NotNull(coordinator);
    }

    [Fact]
    public async Task StartAsync_Persists_Queued_Snapshot_And_Writes_Trace()
    {
        using var container = BuildContainer();
        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var runStore = container.Resolve<InMemoryAgentRunStore>();
        var traceWriter = container.Resolve<CapturingAgentTraceWriter>();
        var objective = CreateObjective("inspect current provider boundary status");
        var request = new AgentRouterStartRunRequest(
            Objective: objective,
            Metadata: AgentRouterMetadata.Empty);

        var result = TestFin.Success(await coordinator.StartAsync(in request, TestContext.Current.CancellationToken));
        var snapshot = TestFin.Success(await runStore.GetSnapshotAsync(result.RunId, TestContext.Current.CancellationToken));

        Assert.False(result.RunId.IsEmpty);
        Assert.Equal(AgentRunStatuses.Queued, result.Status);
        Assert.Equal(result.RunId, snapshot.RunId);
        Assert.Equal(objective, snapshot.Objective);
        Assert.Equal(AgentRunStatuses.Queued, snapshot.Status);
        Assert.Contains("queued", snapshot.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(traceWriter.Snapshots);
        Assert.Equal(result.RunId, traceWriter.Snapshots[0].RunId);
    }


    [Fact]
    public async Task StartAsync_Enqueues_Work_Item_When_Run_Queue_Is_Registered()
    {
        using var container = BuildContainer(registerQueue: true);
        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var runQueue = container.Resolve<CapturingAgentRunQueue>();
        var objective = CreateObjective("queue deterministic SSH workflow");
        var request = new AgentRouterStartRunRequest(
            Objective: objective,
            Metadata: AgentRouterMetadata.Empty);

        var result = TestFin.Success(await coordinator.StartAsync(in request, TestContext.Current.CancellationToken));

        Assert.Single(runQueue.WorkItems);
        Assert.Equal(result.RunId, runQueue.WorkItems[0].RunId);
        Assert.Equal(objective, runQueue.WorkItems[0].Objective);
    }

    [Fact]
    public async Task StartAsync_Rejects_Empty_Objective()
    {
        using var container = BuildContainer();
        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var request = new AgentRouterStartRunRequest(
            Objective: default,
            Metadata: AgentRouterMetadata.Empty);

        var failure = TestFin.Failure(await coordinator.StartAsync(in request, TestContext.Current.CancellationToken));

        Assert.Contains("objective", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSnapshotAsync_Fails_When_Run_Does_Not_Exist()
    {
        using var container = BuildContainer();
        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var runId = AgentRunId.New();

        var failure = TestFin.Failure(await coordinator.GetSnapshotAsync(runId, TestContext.Current.CancellationToken));

        Assert.Contains("not found", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelAsync_Updates_Snapshot_To_Cancelled_And_Writes_Trace()
    {
        using var container = BuildContainer();
        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var runStore = container.Resolve<InMemoryAgentRunStore>();
        var traceWriter = container.Resolve<CapturingAgentTraceWriter>();
        var objective = CreateObjective("cancel this agent router run");
        var startRequest = new AgentRouterStartRunRequest(
            Objective: objective,
            Metadata: AgentRouterMetadata.Empty);
        var startResult = TestFin.Success(await coordinator.StartAsync(in startRequest, TestContext.Current.CancellationToken));

        TestFin.Success(await coordinator.CancelAsync(startResult.RunId, TestContext.Current.CancellationToken));
        var snapshot = TestFin.Success(await runStore.GetSnapshotAsync(startResult.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(AgentRunStatuses.Cancelled, snapshot.Status);
        Assert.Contains("cancelled", snapshot.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, traceWriter.Snapshots.Count);
        Assert.Equal(AgentRunStatuses.Cancelled, traceWriter.Snapshots[^1].Status);
    }

    private static IContainer BuildContainer(bool registerQueue = false)
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new AgentRouterApplicationModule());
        builder.RegisterType<InMemoryAgentRunStore>()
            .AsSelf()
            .As<IAgentRunStore>()
            .SingleInstance();
        builder.RegisterType<CapturingAgentTraceWriter>()
            .AsSelf()
            .As<IAgentTraceWriter>()
            .SingleInstance();

        if (registerQueue)
        {
            builder.RegisterType<CapturingAgentRunQueue>()
                .AsSelf()
                .As<IAgentRunQueue>()
                .SingleInstance();
        }

        return builder.Build();
    }

    private static AgentObjective CreateObjective(string value)
    {
        if (!AgentObjective.TryCreate(value, out var objective))
        {
            throw new InvalidOperationException("The test objective is invalid.");
        }

        return objective;
    }

    private sealed class InMemoryAgentRunStore : IAgentRunStore
    {
        private readonly ConcurrentDictionary<string, AgentRunSnapshot> _snapshots = new(StringComparer.Ordinal);

        public ValueTask<Fin<Unit>> TryCreateAsync(
            AgentRunSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (snapshot.RunId.IsEmpty)
            {
                return new ValueTask<Fin<Unit>>(Fin.Fail<Unit>(Error.New("agent run id is required.")));
            }

            return _snapshots.TryAdd(snapshot.RunId.Value, snapshot)
                ? new ValueTask<Fin<Unit>>(Fin.Succ(default(Unit)))
                : new ValueTask<Fin<Unit>>(Fin.Fail<Unit>(Error.New($"agent run '{snapshot.RunId.Value}' already exists.")));
        }

        public ValueTask<Fin<Unit>> TryUpdateAsync(
            AgentRunSnapshot snapshot,
            long expectedVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (snapshot.RunId.IsEmpty)
            {
                return new ValueTask<Fin<Unit>>(Fin.Fail<Unit>(Error.New("agent run id is required.")));
            }

            if (!_snapshots.TryGetValue(snapshot.RunId.Value, out var current))
            {
                return new ValueTask<Fin<Unit>>(Fin.Fail<Unit>(Error.New($"agent run '{snapshot.RunId.Value}' was not found.")));
            }

            if (current.Version != expectedVersion)
            {
                return new ValueTask<Fin<Unit>>(Fin.Fail<Unit>(Error.New(
                    $"agent run '{snapshot.RunId.Value}' concurrency conflict. Expected version {expectedVersion}, actual version {current.Version}.")));
            }

            _snapshots[snapshot.RunId.Value] = snapshot;
            return new ValueTask<Fin<Unit>>(Fin.Succ(default(Unit)));
        }

        public ValueTask<Fin<AgentRunSnapshot>> GetSnapshotAsync(
            AgentRunId runId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (runId.IsEmpty)
            {
                return new ValueTask<Fin<AgentRunSnapshot>>(
                    Fin.Fail<AgentRunSnapshot>(Error.New("agent run id is required.")));
            }

            if (_snapshots.TryGetValue(runId.Value, out var snapshot))
            {
                return new ValueTask<Fin<AgentRunSnapshot>>(Fin.Succ(snapshot));
            }

            return new ValueTask<Fin<AgentRunSnapshot>>(
                Fin.Fail<AgentRunSnapshot>(Error.New($"agent run '{runId.Value}' was not found.")));
        }
    }

    private sealed class CapturingAgentTraceWriter : IAgentTraceWriter
    {
        private readonly List<AgentRunSnapshot> _snapshots = [];

        public IReadOnlyList<AgentRunSnapshot> Snapshots => _snapshots;

        public ValueTask<Fin<Unit>> WriteSnapshotAsync(
            AgentRunSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshots.Add(snapshot);
            return new ValueTask<Fin<Unit>>(Fin.Succ(default(Unit)));
        }
    }

    private sealed class CapturingAgentRunQueue : IAgentRunQueue
    {
        private readonly List<AgentRunWorkItem> _workItems = [];

        public IReadOnlyList<AgentRunWorkItem> WorkItems => _workItems;

        public ValueTask<Fin<Unit>> EnqueueAsync(
            AgentRunWorkItem workItem,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _workItems.Add(workItem);
            return new ValueTask<Fin<Unit>>(Fin.Succ(default(Unit)));
        }

        public ValueTask<Fin<AgentRunWorkItem>> DequeueAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Fin<AgentRunWorkItem>>(
                Fin.Fail<AgentRunWorkItem>(Error.New("capturing queue does not support dequeue.")));
        }
    }
}
