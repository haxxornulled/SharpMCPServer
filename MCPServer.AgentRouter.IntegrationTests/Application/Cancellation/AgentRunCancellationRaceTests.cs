using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.Execution.Abstractions.Models;
using MCPServer.AgentRouter.Application.Options;
using MCPServer.AgentRouter.Application.Services;
using MCPServer.AgentRouter.Domain.Capabilities;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Policies;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Infrastructure.Queues;
using MCPServer.AgentRouter.Infrastructure.Stores;
using MCPServer.AgentRouter.Infrastructure.Tracing;
using Xunit;
using MCPServer.Execution.Abstractions;

namespace MCPServer.AgentRouter.IntegrationTests.Application.Cancellation;

public sealed class AgentRunCancellationRaceTests
{
    private const string LowRiskCapability = "test.low-risk";
    private const string ApprovalCapability = "test.approval-required";

    [Fact]
    public async Task Cancel_Queued_Run_Terminalizes_Run_And_Stale_Queued_Work_Does_Not_Execute_Plugin()
    {
        var plugin = TestAgentPlugin.Immediate(LowRiskCapability, requiresApproval: false);
        var runtime = CreateRuntime(plugin);
        var startRequest = CreateStartRequest(LowRiskCapability, "cancel queued run");

        var start = Success(await runtime.Coordinator.StartAsync(in startRequest, TestContext.Current.CancellationToken));
        var cancel = await runtime.Coordinator.CancelAsync(start.RunId, TestContext.Current.CancellationToken);
        Success(cancel);

        var workerResult = Success(await runtime.Worker.RunCycleAsync(TestContext.Current.CancellationToken));
        var snapshot = Success(await runtime.Store.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(1, workerResult.ProcessedCount);
        Assert.Equal(AgentRunStatuses.Cancelled, snapshot.Status);
        Assert.Equal(0, plugin.ExecuteCount);
        Assert.Equal(
            new[] { AgentRunStatuses.Queued, AgentRunStatuses.Cancelled },
            TraceStatuses(runtime.TraceWriter, start.RunId));
    }

    [Fact]
    public async Task Cancel_AwaitingApproval_Run_Terminalizes_Run_And_Approval_No_Longer_Requeues()
    {
        var plugin = TestAgentPlugin.Immediate(ApprovalCapability, requiresApproval: true);
        var runtime = CreateRuntime(plugin);
        var startRequest = CreateStartRequest(ApprovalCapability, "cancel awaiting approval run");

        var start = Success(await runtime.Coordinator.StartAsync(in startRequest, TestContext.Current.CancellationToken));
        var firstCycle = Success(await runtime.Worker.RunCycleAsync(TestContext.Current.CancellationToken));
        var awaitingApproval = Success(await runtime.Store.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(1, firstCycle.ProcessedCount);
        Assert.Equal(AgentRunStatuses.AwaitingApproval, awaitingApproval.Status);
        Assert.Equal(0, plugin.ExecuteCount);

        var cancel = await runtime.Coordinator.CancelAsync(start.RunId, TestContext.Current.CancellationToken);
        Success(cancel);

        var approveRequest = new AgentRouterApproveRunRequest(
            RunId: start.RunId,
            ApprovalId: "approval-after-cancel-should-fail",
            ApprovedBy: "integration-test",
            Metadata: null);
        var approveResult = await runtime.Coordinator.ApproveAsync(in approveRequest, TestContext.Current.CancellationToken);
        var approveError = Failure(approveResult);
        var cancelled = Success(await runtime.Store.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Contains("not awaiting approval", approveError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AgentRunStatuses.Cancelled, cancelled.Status);
        Assert.Equal(0, plugin.ExecuteCount);
        Assert.Equal(
            new[]
            {
                AgentRunStatuses.Queued,
                AgentRunStatuses.Planning,
                AgentRunStatuses.AwaitingApproval,
                AgentRunStatuses.Cancelled
            },
            TraceStatuses(runtime.TraceWriter, start.RunId));
    }

    [Fact]
    public async Task Cancel_Working_Run_Does_Not_Let_Plugin_Completion_Overwrite_Cancelled_Status()
    {
        var plugin = TestAgentPlugin.Blocking(LowRiskCapability);
        var runtime = CreateRuntime(plugin);
        var startRequest = CreateStartRequest(LowRiskCapability, "cancel working run");

        var start = Success(await runtime.Coordinator.StartAsync(in startRequest, TestContext.Current.CancellationToken));
        var workerTask = runtime.Worker.RunCycleAsync(TestContext.Current.CancellationToken).AsTask();
        await plugin.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var working = Success(await runtime.Store.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));
        Assert.Equal(AgentRunStatuses.Working, working.Status);
        Assert.Equal(1, plugin.ExecuteCount);

        var cancel = await runtime.Coordinator.CancelAsync(start.RunId, TestContext.Current.CancellationToken);
        Success(cancel);

        plugin.CompleteSuccessfully("plugin completed after cancellation");
        var workerResult = Success(await workerTask);
        var finalSnapshot = Success(await runtime.Store.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(1, workerResult.ProcessedCount);
        Assert.Equal(AgentRunStatuses.Cancelled, finalSnapshot.Status);
        Assert.Contains("cancelled", finalSnapshot.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AgentRunStatuses.Completed, TraceStatuses(runtime.TraceWriter, start.RunId));
        Assert.Equal(
            new[]
            {
                AgentRunStatuses.Queued,
                AgentRunStatuses.Planning,
                AgentRunStatuses.Working,
                AgentRunStatuses.Cancelled
            },
            TraceStatuses(runtime.TraceWriter, start.RunId));
    }

    private static TestRuntime CreateRuntime(IAgentPlugin plugin)
    {
        var store = new InMemoryAgentRunStore();
        var traceWriter = new InMemoryAgentTraceWriter();
        var queue = new BoundedChannelAgentRunQueue(new AgentRouterConcurrencyOptions
        {
            MaxQueuedRuns = 16,
            MaxConcurrentRuns = 1,
            MaxConcurrentStepsPerRun = 1,
            QueueFullMode = AgentRunQueueFullModes.Reject
        });
        var queues = new IAgentRunQueue[] { queue };
        var coordinator = new DefaultAgentRunCoordinator(store, traceWriter, queues);
        var registry = new DefaultAgentPluginRegistry([plugin]);
        var policyEvaluator = new DefaultAgentPluginPolicyEvaluator();
        var executor = new DefaultAgentRunExecutor(store, traceWriter, registry, policyEvaluator);
        var worker = new DefaultAgentRouterWorker(executor, queues);

        return new TestRuntime(coordinator, worker, store, traceWriter);
    }

    private static AgentRouterStartRunRequest CreateStartRequest(string capabilityName, string objectiveText)
    {
        return new AgentRouterStartRunRequest(
            Objective: CreateObjective(objectiveText),
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.Capability] = capabilityName
            });
    }

    private static AgentObjective CreateObjective(string value)
    {
        if (AgentObjective.TryCreate(value, out var objective))
        {
            return objective;
        }

        throw new InvalidOperationException($"Invalid test objective '{value}'.");
    }

    private static IReadOnlyList<string> TraceStatuses(
        InMemoryAgentTraceWriter traceWriter,
        AgentRunId runId)
    {
        return traceWriter.Snapshots
            .Where(snapshot => snapshot.RunId == runId)
            .Select(static snapshot => snapshot.Status)
            .ToArray();
    }

    private static T Success<T>(Fin<T> result)
    {
        return result.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));
    }

    private static Error Failure<T>(Fin<T> result)
    {
        return result.Match(
            Succ: static _ => throw new InvalidOperationException("Expected failure, but operation succeeded."),
            Fail: static error => error);
    }

    private sealed record TestRuntime(
        IAgentRunCoordinator Coordinator,
        IAgentRouterWorker Worker,
        IAgentRunStore Store,
        InMemoryAgentTraceWriter TraceWriter);

    private sealed class TestAgentPlugin : IAgentPlugin
    {
        private readonly string _capabilityName;
        private readonly TaskCompletionSource<AgentPluginExecutionResult>? _completion;
        private int _executeCount;

        private TestAgentPlugin(
            string capabilityName,
            bool requiresApproval,
            TaskCompletionSource<AgentPluginExecutionResult>? completion)
        {
            _capabilityName = capabilityName;
            _completion = completion;
            Capabilities =
            [
                AgentCapabilityDescriptor.Create(
                    capabilityName,
                    displayName: capabilityName,
                    riskLevel: requiresApproval ? AgentExecutionRiskLevels.Critical : AgentExecutionRiskLevels.Low,
                    requiresApproval: requiresApproval)
            ];
        }

        public string Name => "test-agent-plugin";

        public IReadOnlyList<AgentCapabilityDescriptor> Capabilities { get; }

        public int ExecuteCount => Volatile.Read(ref _executeCount);

        public TaskCompletionSource<AgentPluginExecutionRequest> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static TestAgentPlugin Immediate(string capabilityName, bool requiresApproval)
        {
            return new TestAgentPlugin(capabilityName, requiresApproval, completion: null);
        }

        public static TestAgentPlugin Blocking(string capabilityName)
        {
            return new TestAgentPlugin(
                capabilityName,
                requiresApproval: false,
                completion: new TaskCompletionSource<AgentPluginExecutionResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously));
        }

        public bool CanHandle(AgentPluginExecutionRequest request)
        {
            return string.Equals(request.CapabilityName, _capabilityName, StringComparison.OrdinalIgnoreCase);
        }

        public async ValueTask<Fin<AgentPluginExecutionResult>> ExecuteAsync(
            AgentPluginExecutionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _executeCount);
            Started.TrySetResult(request);

            if (_completion is null)
            {
                return Fin.Succ(AgentPluginExecutionResult.Success(
                    status: AgentRunStatuses.Completed,
                    message: "test plugin completed.",
                    externalRunId: null));
            }

            var result = await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return Fin.Succ(result);
        }

        public void CompleteSuccessfully(string message)
        {
            if (_completion is null)
            {
                throw new InvalidOperationException("This test plugin is not blocking.");
            }

            _completion.TrySetResult(AgentPluginExecutionResult.Success(
                status: AgentRunStatuses.Completed,
                message: message,
                externalRunId: null));
        }
    }
}
