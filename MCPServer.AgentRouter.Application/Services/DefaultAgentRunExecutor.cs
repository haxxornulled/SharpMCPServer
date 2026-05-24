using System.Diagnostics.CodeAnalysis;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Application.WorkItems;
using MCPServer.AgentRouter.Domain.Capabilities;
using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Application.Services;

public sealed class DefaultAgentRunExecutor : IAgentRunExecutor
{
    private const string CapabilityMissingMessage = "Agent capability is required. Use metadata key 'agent.capability'.";

    private readonly IAgentRunStore _runStore;
    private readonly IAgentTraceWriter _traceWriter;
    private readonly IAgentPluginRegistry _pluginRegistry;
    private readonly IAgentPluginPolicyEvaluator _policyEvaluator;

    public DefaultAgentRunExecutor(
        IAgentRunStore runStore,
        IAgentTraceWriter traceWriter,
        IAgentPluginRegistry pluginRegistry,
        IAgentPluginPolicyEvaluator policyEvaluator)
    {
        _runStore = runStore ?? throw new ArgumentNullException(nameof(runStore));
        _traceWriter = traceWriter ?? throw new ArgumentNullException(nameof(traceWriter));
        _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
        _policyEvaluator = policyEvaluator ?? throw new ArgumentNullException(nameof(policyEvaluator));
    }

    public ValueTask<Fin<AgentRouterWorkerCycleResult>> ExecuteAsync(
        AgentRunWorkItem workItem,
        CancellationToken cancellationToken)
    {
        return ExecuteCoreAsync(workItem, cancellationToken);
    }

    private async ValueTask<Fin<AgentRouterWorkerCycleResult>> ExecuteCoreAsync(
        AgentRunWorkItem workItem,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!workItem.IsValid)
        {
            return Fin.Fail<AgentRouterWorkerCycleResult>(Error.New("agent run work item requires a valid run id and objective."));
        }

        var getResult = await _runStore.GetSnapshotAsync(workItem.RunId, cancellationToken).ConfigureAwait(false);
        if (TryGetError(getResult, out var getError))
        {
            return Fin.Fail<AgentRouterWorkerCycleResult>(getError);
        }

        var snapshot = getResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        AgentRun run;
        try
        {
            run = AgentRun.Rehydrate(in snapshot);
        }
        catch (ArgumentException exception)
        {
            return Fin.Fail<AgentRouterWorkerCycleResult>(Error.New(exception.Message));
        }

        if (run.IsTerminal)
        {
            return Fin.Succ(AgentRouterWorkerCycleResult.Processed(
                processedCount: 1,
                message: $"AgentRouter run '{run.RunId.Value}' is already terminal with status '{run.Status}'."));
        }

        var planning = await PersistTransitionAsync(
            run,
            run.MarkPlanning(DateTimeOffset.UtcNow, "AgentRouter run planning started."),
            cancellationToken).ConfigureAwait(false);

        if (TryGetError(planning, out var planningError))
        {
            return Fin.Fail<AgentRouterWorkerCycleResult>(planningError);
        }

        var metadata = workItem.MetadataOrEmpty;
        var capabilityName = ResolveCapabilityName(metadata);
        if (string.IsNullOrWhiteSpace(capabilityName))
        {
            return await FailRunAsProcessedAsync(run, CapabilityMissingMessage, cancellationToken).ConfigureAwait(false);
        }

        var pluginRequest = new AgentPluginExecutionRequest(
            workItem.RunId,
            workItem.Objective,
            capabilityName,
            metadata);

        var selectResult = await _pluginRegistry.SelectAsync(pluginRequest, cancellationToken).ConfigureAwait(false);
        if (TryGetError(selectResult, out var selectError))
        {
            return await FailRunAsProcessedAsync(run, selectError.Message, cancellationToken).ConfigureAwait(false);
        }

        var plugin = selectResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        if (!TryFindCapability(plugin, capabilityName, out var capability))
        {
            return await FailRunAsProcessedAsync(
                run,
                $"Plugin '{plugin.Name}' did not expose descriptor for capability '{capabilityName}'.",
                cancellationToken).ConfigureAwait(false);
        }

        var policyRequest = new AgentPluginPolicyRequest(pluginRequest, capability);
        var policyResult = await _policyEvaluator.EvaluateAsync(policyRequest, cancellationToken).ConfigureAwait(false);
        if (TryGetError(policyResult, out var policyError))
        {
            return await FailRunAsProcessedAsync(run, policyError.Message, cancellationToken).ConfigureAwait(false);
        }

        var policyDecision = policyResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        if (policyDecision.RequiresApproval)
        {
            var awaitingApproval = await PersistTransitionAsync(
                run,
                run.MarkAwaitingApproval(DateTimeOffset.UtcNow, policyDecision.Reason ?? "AgentRouter run is awaiting approval."),
                cancellationToken).ConfigureAwait(false);

            if (TryGetError(awaitingApproval, out var approvalError))
            {
                return Fin.Fail<AgentRouterWorkerCycleResult>(approvalError);
            }

            return Fin.Succ(AgentRouterWorkerCycleResult.Processed(
                processedCount: 1,
                message: $"AgentRouter run '{run.RunId.Value}' is awaiting approval for capability '{capabilityName}'."));
        }

        if (!policyDecision.IsAllowed)
        {
            return await FailRunAsProcessedAsync(
                run,
                policyDecision.Reason ?? $"AgentRouter policy rejected capability '{capabilityName}'.",
                cancellationToken).ConfigureAwait(false);
        }

        var working = await PersistTransitionAsync(
            run,
            run.MarkWorking(DateTimeOffset.UtcNow, $"AgentRouter run executing capability '{capabilityName}' with plugin '{plugin.Name}'."),
            cancellationToken).ConfigureAwait(false);

        if (TryGetError(working, out var workingError))
        {
            return Fin.Fail<AgentRouterWorkerCycleResult>(workingError);
        }

        var executionResult = await plugin.ExecuteAsync(pluginRequest, cancellationToken).ConfigureAwait(false);
        if (TryGetError(executionResult, out var executionError))
        {
            return await FailRunAsProcessedAsync(run, executionError.Message, cancellationToken).ConfigureAwait(false);
        }

        var pluginResult = executionResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var finalTransition = pluginResult.Succeeded
            ? run.Complete(DateTimeOffset.UtcNow, pluginResult.Message)
            : run.Fail(DateTimeOffset.UtcNow, pluginResult.Message);

        var finalPersist = await PersistTransitionAsync(run, finalTransition, cancellationToken).ConfigureAwait(false);
        if (TryGetError(finalPersist, out var finalError))
        {
            return Fin.Fail<AgentRouterWorkerCycleResult>(finalError);
        }

        var finalStatus = pluginResult.Succeeded ? AgentRunStatuses.Completed : AgentRunStatuses.Failed;
        return Fin.Succ(AgentRouterWorkerCycleResult.Processed(
            processedCount: 1,
            message: $"AgentRouter run '{run.RunId.Value}' finished with status '{finalStatus}'."));
    }

    private async ValueTask<Fin<AgentRouterWorkerCycleResult>> FailRunAsProcessedAsync(
        AgentRun run,
        string? message,
        CancellationToken cancellationToken)
    {
        var failureMessage = string.IsNullOrWhiteSpace(message)
            ? "AgentRouter run failed."
            : message;

        var failed = await PersistTransitionAsync(
            run,
            run.Fail(DateTimeOffset.UtcNow, failureMessage),
            cancellationToken).ConfigureAwait(false);

        if (TryGetError(failed, out var failedError))
        {
            return Fin.Fail<AgentRouterWorkerCycleResult>(failedError);
        }

        return Fin.Succ(AgentRouterWorkerCycleResult.Processed(
            processedCount: 1,
            message: $"AgentRouter run '{run.RunId.Value}' failed: {failureMessage}"));
    }

    private async ValueTask<Fin<AgentRunSnapshot>> PersistTransitionAsync(
        AgentRun run,
        AgentRunTransitionResult transitionResult,
        CancellationToken cancellationToken)
    {
        if (!transitionResult.Succeeded)
        {
            return Fin.Fail<AgentRunSnapshot>(Error.New(
                transitionResult.Message ?? "Agent run transition was rejected by the domain model."));
        }

        var snapshot = run.ToSnapshot();

        var saveResult = await PersistVersionedSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (TryGetError(saveResult, out var saveError))
        {
            return Fin.Fail<AgentRunSnapshot>(saveError);
        }

        var traceResult = await _traceWriter.WriteSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (TryGetError(traceResult, out var traceError))
        {
            return Fin.Fail<AgentRunSnapshot>(traceError);
        }

        return Fin.Succ(snapshot);
    }

    private ValueTask<Fin<Unit>> PersistVersionedSnapshotAsync(
        AgentRunSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        return snapshot.Version switch
        {
            0 => _runStore.TryCreateAsync(snapshot, cancellationToken),
            > 0 => _runStore.TryUpdateAsync(snapshot, expectedVersion: snapshot.Version - 1, cancellationToken: cancellationToken),
            _ => new ValueTask<Fin<Unit>>(Fin.Fail<Unit>(Error.New("agent run version cannot be negative.")))
        };
    }

    private static string? ResolveCapabilityName(IReadOnlyDictionary<string, string?> metadata)
    {
        return GetMetadataValue(metadata, AgentRouterMetadataKeys.Capability)
            ?? GetMetadataValue(metadata, "capability")
            ?? GetMetadataValue(metadata, "agentCapability");
    }

    private static bool TryFindCapability(
        IAgentPlugin plugin,
        string capabilityName,
        [NotNullWhen(true)] out AgentCapabilityDescriptor? capability)
    {
        foreach (var candidate in plugin.Capabilities)
        {
            if (string.Equals(candidate.Name.Value, capabilityName, StringComparison.OrdinalIgnoreCase))
            {
                capability = candidate;
                return true;
            }
        }

        capability = null;
        return false;
    }

    private static string? GetMetadataValue(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static bool TryGetError<T>(Fin<T> result, [NotNullWhen(true)] out Error? error)
    {
        error = result.Match<Error?>(
            Succ: static _ => null,
            Fail: static failure => failure);

        return error is not null;
    }
}
