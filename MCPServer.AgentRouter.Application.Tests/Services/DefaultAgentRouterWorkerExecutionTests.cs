using System.Collections.Concurrent;
using Autofac;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Application.Tests.Testing;
using MCPServer.AgentRouter.Application.WorkItems;
using MCPServer.AgentRouter.Domain.Capabilities;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Policies;
using MCPServer.AgentRouter.Domain.Runs;
using Xunit;

namespace MCPServer.AgentRouter.Application.Tests.Services;

public sealed class DefaultAgentRouterWorkerExecutionTests
{
    [Fact]
    public async Task RunCycleAsync_Executes_Queued_Run_Through_Selected_Plugin_And_Completes_Run()
    {
        using var container = BuildContainer(new SuccessfulAgentPlugin());
        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var worker = container.Resolve<IAgentRouterWorker>();
        var runStore = container.Resolve<InMemoryAgentRunStore>();
        var traceWriter = container.Resolve<CapturingAgentTraceWriter>();
        var objective = CreateObjective("execute deterministic test capability");
        var request = new AgentRouterStartRunRequest(
            Objective: objective,
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.Capability] = TestCapabilityName
            });

        var start = TestFin.Success(await coordinator.StartAsync(in request, TestContext.Current.CancellationToken));
        var cycle = TestFin.Success(await worker.RunCycleAsync(TestContext.Current.CancellationToken));
        var snapshot = TestFin.Success(await runStore.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(1, cycle.ProcessedCount);
        Assert.Equal(AgentRunStatuses.Completed, snapshot.Status);
        Assert.Contains("completed", snapshot.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Collection(
            traceWriter.Snapshots.Select(static item => item.Status),
            static status => Assert.Equal(AgentRunStatuses.Queued, status),
            static status => Assert.Equal(AgentRunStatuses.Planning, status),
            static status => Assert.Equal(AgentRunStatuses.Working, status),
            static status => Assert.Equal(AgentRunStatuses.Completed, status));
    }

    [Fact]
    public async Task RunCycleAsync_Marks_Run_Awaiting_Approval_When_Selected_Plugin_Capability_Requires_Approval()
    {
        var plugin = new ApprovalRequiredAgentPlugin();
        using var container = BuildContainer(plugin);
        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var worker = container.Resolve<IAgentRouterWorker>();
        var runStore = container.Resolve<InMemoryAgentRunStore>();
        var traceWriter = container.Resolve<CapturingAgentTraceWriter>();
        var objective = CreateObjective("approve critical remote shell before execution");
        var request = new AgentRouterStartRunRequest(
            Objective: objective,
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.Capability] = ApprovalRequiredCapabilityName
            });

        var start = TestFin.Success(await coordinator.StartAsync(in request, TestContext.Current.CancellationToken));
        var cycle = TestFin.Success(await worker.RunCycleAsync(TestContext.Current.CancellationToken));
        var snapshot = TestFin.Success(await runStore.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(1, cycle.ProcessedCount);
        Assert.Equal(AgentRunStatuses.AwaitingApproval, snapshot.Status);
        Assert.Contains("requires approval", snapshot.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, plugin.ExecuteCount);
        Assert.Collection(
            traceWriter.Snapshots.Select(static item => item.Status),
            static status => Assert.Equal(AgentRunStatuses.Queued, status),
            static status => Assert.Equal(AgentRunStatuses.Planning, status),
            static status => Assert.Equal(AgentRunStatuses.AwaitingApproval, status));
    }

    [Fact]
    public async Task RunCycleAsync_Executes_Approval_Required_Plugin_When_Approval_Is_Granted()
    {
        var plugin = new ApprovalRequiredAgentPlugin();
        using var container = BuildContainer(plugin);
        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var worker = container.Resolve<IAgentRouterWorker>();
        var runStore = container.Resolve<InMemoryAgentRunStore>();
        var objective = CreateObjective("execute approved critical remote shell capability");
        var request = new AgentRouterStartRunRequest(
            Objective: objective,
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.Capability] = ApprovalRequiredCapabilityName,
                [AgentRouterMetadataKeys.ApprovalGranted] = "true",
                [AgentRouterMetadataKeys.ApprovalId] = "approval-test-1"
            });

        var start = TestFin.Success(await coordinator.StartAsync(in request, TestContext.Current.CancellationToken));
        var cycle = TestFin.Success(await worker.RunCycleAsync(TestContext.Current.CancellationToken));
        var snapshot = TestFin.Success(await runStore.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(1, cycle.ProcessedCount);
        Assert.Equal(AgentRunStatuses.Completed, snapshot.Status);
        Assert.Equal(1, plugin.ExecuteCount);
    }


    [Fact]
    public async Task ApproveAsync_Requeues_Awaiting_Approval_Run_And_Worker_Executes_It()
    {
        var plugin = new ApprovalRequiredAgentPlugin();
        using var container = BuildContainer(plugin);
        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var worker = container.Resolve<IAgentRouterWorker>();
        var runStore = container.Resolve<InMemoryAgentRunStore>();
        var traceWriter = container.Resolve<CapturingAgentTraceWriter>();
        var objective = CreateObjective("approve and resume critical remote shell capability");
        var request = new AgentRouterStartRunRequest(
            Objective: objective,
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.Capability] = ApprovalRequiredCapabilityName
            });

        var start = TestFin.Success(await coordinator.StartAsync(in request, TestContext.Current.CancellationToken));
        TestFin.Success(await worker.RunCycleAsync(TestContext.Current.CancellationToken));

        var approvalRequest = new AgentRouterApproveRunRequest(
            RunId: start.RunId,
            ApprovalId: "approval-resume-test-1",
            ApprovedBy: "unit-test",
            Metadata: null);

        var approved = TestFin.Success(await coordinator.ApproveAsync(in approvalRequest, TestContext.Current.CancellationToken));
        var resumedCycle = TestFin.Success(await worker.RunCycleAsync(TestContext.Current.CancellationToken));
        var snapshot = TestFin.Success(await runStore.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(AgentRunStatuses.Queued, approved.Status);
        Assert.Equal(1, resumedCycle.ProcessedCount);
        Assert.Equal(AgentRunStatuses.Completed, snapshot.Status);
        Assert.Equal(1, plugin.ExecuteCount);
        Assert.Equal("true", snapshot.MetadataOrEmpty[AgentRouterMetadataKeys.ApprovalGranted]);
        Assert.Equal("approval-resume-test-1", snapshot.MetadataOrEmpty[AgentRouterMetadataKeys.ApprovalId]);
        Assert.Collection(
            traceWriter.Snapshots.Select(static item => item.Status),
            static status => Assert.Equal(AgentRunStatuses.Queued, status),
            static status => Assert.Equal(AgentRunStatuses.Planning, status),
            static status => Assert.Equal(AgentRunStatuses.AwaitingApproval, status),
            static status => Assert.Equal(AgentRunStatuses.Queued, status),
            static status => Assert.Equal(AgentRunStatuses.Planning, status),
            static status => Assert.Equal(AgentRunStatuses.Working, status),
            static status => Assert.Equal(AgentRunStatuses.Completed, status));
    }

    [Fact]
    public async Task RunCycleAsync_Fails_Run_When_Capability_Metadata_Is_Missing()
    {
        using var container = BuildContainer(new SuccessfulAgentPlugin());
        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var worker = container.Resolve<IAgentRouterWorker>();
        var runStore = container.Resolve<InMemoryAgentRunStore>();
        var objective = CreateObjective("missing capability should fail closed");
        var request = new AgentRouterStartRunRequest(
            Objective: objective,
            Metadata: AgentRouterMetadata.Empty);

        var start = TestFin.Success(await coordinator.StartAsync(in request, TestContext.Current.CancellationToken));
        var cycle = TestFin.Success(await worker.RunCycleAsync(TestContext.Current.CancellationToken));
        var snapshot = TestFin.Success(await runStore.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(1, cycle.ProcessedCount);
        Assert.Equal(AgentRunStatuses.Failed, snapshot.Status);
        Assert.Contains("capability", snapshot.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunCycleAsync_Fails_Run_When_Selected_Plugin_Fails()
    {
        using var container = BuildContainer(new FailingAgentPlugin());
        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var worker = container.Resolve<IAgentRouterWorker>();
        var runStore = container.Resolve<InMemoryAgentRunStore>();
        var objective = CreateObjective("plugin execution failure should fail run");
        var request = new AgentRouterStartRunRequest(
            Objective: objective,
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.Capability] = TestCapabilityName
            });

        var start = TestFin.Success(await coordinator.StartAsync(in request, TestContext.Current.CancellationToken));
        var cycle = TestFin.Success(await worker.RunCycleAsync(TestContext.Current.CancellationToken));
        var snapshot = TestFin.Success(await runStore.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(1, cycle.ProcessedCount);
        Assert.Equal(AgentRunStatuses.Failed, snapshot.Status);
        Assert.Contains("test plugin failed", snapshot.Message, StringComparison.OrdinalIgnoreCase);
    }

    private const string TestCapabilityName = "test-capability";
    private const string ApprovalRequiredCapabilityName = "approval-required-capability";

    private static IContainer BuildContainer(IAgentPlugin plugin)
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
        builder.RegisterType<InMemoryAgentRunQueue>()
            .AsSelf()
            .As<IAgentRunQueue>()
            .SingleInstance();
        builder.RegisterInstance(plugin)
            .As<IAgentPlugin>()
            .SingleInstance();

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

    private sealed class InMemoryAgentRunQueue : IAgentRunQueue
    {
        private readonly Queue<AgentRunWorkItem> _items = new();

        public ValueTask<Fin<Unit>> EnqueueAsync(
            AgentRunWorkItem workItem,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _items.Enqueue(workItem);
            return new ValueTask<Fin<Unit>>(Fin.Succ(default(Unit)));
        }

        public ValueTask<Fin<AgentRunWorkItem>> DequeueAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_items.TryDequeue(out var workItem))
            {
                return new ValueTask<Fin<AgentRunWorkItem>>(Fin.Succ(workItem));
            }

            return new ValueTask<Fin<AgentRunWorkItem>>(
                Fin.Fail<AgentRunWorkItem>(Error.New("agent run queue is empty.")));
        }
    }

    private sealed class SuccessfulAgentPlugin : IAgentPlugin
    {
        public string Name => "test";

        public IReadOnlyList<AgentCapabilityDescriptor> Capabilities { get; } =
        [
            AgentCapabilityDescriptor.Create(
                TestCapabilityName,
                "Test capability",
                AgentExecutionRiskLevels.Low,
                requiresApproval: false)
        ];

        public bool CanHandle(AgentPluginExecutionRequest request)
        {
            return string.Equals(request.CapabilityName, TestCapabilityName, StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<Fin<AgentPluginExecutionResult>> ExecuteAsync(
            AgentPluginExecutionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Fin<AgentPluginExecutionResult>>(
                Fin.Succ(AgentPluginExecutionResult.Success("completed", "Test plugin completed.", null)));
        }
    }

    private sealed class ApprovalRequiredAgentPlugin : IAgentPlugin
    {
        public string Name => "approval-required-test";

        public int ExecuteCount { get; private set; }

        public IReadOnlyList<AgentCapabilityDescriptor> Capabilities { get; } =
        [
            AgentCapabilityDescriptor.Create(
                ApprovalRequiredCapabilityName,
                "Approval required capability",
                AgentExecutionRiskLevels.Critical,
                requiresApproval: true)
        ];

        public bool CanHandle(AgentPluginExecutionRequest request)
        {
            return string.Equals(request.CapabilityName, ApprovalRequiredCapabilityName, StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<Fin<AgentPluginExecutionResult>> ExecuteAsync(
            AgentPluginExecutionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCount++;
            return new ValueTask<Fin<AgentPluginExecutionResult>>(
                Fin.Succ(AgentPluginExecutionResult.Success("completed", "Approved plugin completed.", null)));
        }
    }

    private sealed class FailingAgentPlugin : IAgentPlugin
    {
        public string Name => "test";

        public IReadOnlyList<AgentCapabilityDescriptor> Capabilities { get; } =
        [
            AgentCapabilityDescriptor.Create(
                TestCapabilityName,
                "Test capability",
                AgentExecutionRiskLevels.Low,
                requiresApproval: false)
        ];

        public bool CanHandle(AgentPluginExecutionRequest request)
        {
            return string.Equals(request.CapabilityName, TestCapabilityName, StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<Fin<AgentPluginExecutionResult>> ExecuteAsync(
            AgentPluginExecutionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Fin<AgentPluginExecutionResult>>(
                Fin.Fail<AgentPluginExecutionResult>(Error.New("test plugin failed")));
        }
    }
}
