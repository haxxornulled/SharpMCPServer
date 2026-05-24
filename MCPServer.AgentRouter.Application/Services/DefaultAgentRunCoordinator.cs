using System.Diagnostics.CodeAnalysis;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Application.WorkItems;
using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Application.Services;

public sealed class DefaultAgentRunCoordinator : IAgentRunCoordinator
{
    private const string QueuedMessage = "AgentRouter run queued.";
    private const string CancelledMessage = "AgentRouter run cancelled.";
    private const string ApprovedMessage = "AgentRouter run approved and re-queued.";
    private const string ApprovalApprovedByKey = "agent.approval.approvedBy";

    private static readonly Fin<Unit> UnitSuccess = Fin.Succ(default(Unit));

    private readonly IAgentRunStore _runStore;
    private readonly IAgentTraceWriter _traceWriter;
    private readonly IAgentRunQueue? _runQueue;

    public DefaultAgentRunCoordinator(
        IAgentRunStore runStore,
        IAgentTraceWriter traceWriter,
        IEnumerable<IAgentRunQueue> runQueues)
    {
        _runStore = runStore ?? throw new ArgumentNullException(nameof(runStore));
        _traceWriter = traceWriter ?? throw new ArgumentNullException(nameof(traceWriter));
        ArgumentNullException.ThrowIfNull(runQueues);

        _runQueue = runQueues.FirstOrDefault();
    }

    public ValueTask<Fin<AgentRouterStartRunResult>> StartAsync(
        in AgentRouterStartRunRequest request,
        CancellationToken cancellationToken)
    {
        var capturedRequest = request;
        return StartCoreAsync(capturedRequest, cancellationToken);
    }

    public ValueTask<Fin<AgentRunSnapshot>> GetSnapshotAsync(
        AgentRunId runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return runId.IsEmpty
            ? new ValueTask<Fin<AgentRunSnapshot>>(Fin.Fail<AgentRunSnapshot>(Error.New("agent run id is required.")))
            : _runStore.GetSnapshotAsync(runId, cancellationToken);
    }

    public ValueTask<Fin<AgentRunSnapshot>> ApproveAsync(
        in AgentRouterApproveRunRequest request,
        CancellationToken cancellationToken)
    {
        var capturedRequest = request;
        return ApproveCoreAsync(capturedRequest, cancellationToken);
    }

    public ValueTask<Fin<Unit>> CancelAsync(
        AgentRunId runId,
        CancellationToken cancellationToken)
    {
        return CancelCoreAsync(runId, cancellationToken);
    }

    private async ValueTask<Fin<AgentRouterStartRunResult>> StartCoreAsync(
        AgentRouterStartRunRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateStartRequest(request) is { } validationError)
        {
            return Fin.Fail<AgentRouterStartRunResult>(validationError);
        }

        var now = DateTimeOffset.UtcNow;
        var runId = AgentRunId.New();
        var run = AgentRun.Queue(runId, request.Objective, now, QueuedMessage, request.MetadataOrEmpty);
        var snapshot = run.ToSnapshot();

        var dispatchResult = await PersistAndDispatchAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (TryGetError(dispatchResult, out var dispatchError))
        {
            return Fin.Fail<AgentRouterStartRunResult>(dispatchError);
        }

        return Fin.Succ(new AgentRouterStartRunResult(
            RunId: runId,
            Status: snapshot.Status,
            Message: snapshot.Message));
    }

    private async ValueTask<Fin<AgentRunSnapshot>> ApproveCoreAsync(
        AgentRouterApproveRunRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateApproveRequest(request) is { } validationError)
        {
            return Fin.Fail<AgentRunSnapshot>(validationError);
        }

        var loadResult = await LoadRunAsync(request.RunId, cancellationToken).ConfigureAwait(false);
        if (TryGetError(loadResult, out var loadError))
        {
            return Fin.Fail<AgentRunSnapshot>(loadError);
        }

        var run = UnsafeValue(loadResult);
        if (run.Status is not AgentRunStatuses.AwaitingApproval)
        {
            return Fin.Fail<AgentRunSnapshot>(Error.New(
                $"agent run '{request.RunId.Value}' is not awaiting approval. Current status is '{run.Status}'."));
        }

        var approvalMetadata = BuildApprovalMetadata(request);
        var transitionResult = run.Approve(DateTimeOffset.UtcNow, request.ApprovalId, approvalMetadata, ApprovedMessage);
        if (!transitionResult.Succeeded)
        {
            return Fin.Fail<AgentRunSnapshot>(Error.New(
                transitionResult.Message ?? "Agent run approval was rejected by the domain model."));
        }

        var approvedSnapshot = run.ToSnapshot();
        var dispatchResult = await PersistAndDispatchAsync(approvedSnapshot, cancellationToken).ConfigureAwait(false);
        if (TryGetError(dispatchResult, out var dispatchError))
        {
            return Fin.Fail<AgentRunSnapshot>(dispatchError);
        }

        return Fin.Succ(approvedSnapshot);
    }

    private async ValueTask<Fin<Unit>> CancelCoreAsync(
        AgentRunId runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (runId.IsEmpty)
        {
            return Fin.Fail<Unit>(Error.New("agent run id is required."));
        }

        var loadResult = await LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (TryGetError(loadResult, out var loadError))
        {
            return Fin.Fail<Unit>(loadError);
        }

        var run = UnsafeValue(loadResult);
        var transitionResult = run.Cancel(DateTimeOffset.UtcNow, CancelledMessage);
        if (!transitionResult.Succeeded)
        {
            return Fin.Fail<Unit>(Error.New(
                transitionResult.Message ?? "Agent run cancellation was rejected by the domain model."));
        }

        return await PersistSnapshotAsync(run.ToSnapshot(), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Fin<AgentRun>> LoadRunAsync(
        AgentRunId runId,
        CancellationToken cancellationToken)
    {
        var getResult = await _runStore.GetSnapshotAsync(runId, cancellationToken).ConfigureAwait(false);

        return getResult.Match(
            Succ: static snapshot => RehydrateSnapshot(snapshot),
            Fail: static error => Fin.Fail<AgentRun>(error));
    }

    private async ValueTask<Fin<Unit>> PersistAndDispatchAsync(
        AgentRunSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var persistResult = await PersistSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (TryGetError(persistResult, out var persistError))
        {
            return Fin.Fail<Unit>(persistError);
        }

        if (_runQueue is null)
        {
            return UnitSuccess;
        }

        var enqueueResult = await EnqueueSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (!TryGetError(enqueueResult, out var enqueueError))
        {
            return UnitSuccess;
        }

        await MarkDispatchFailedAsync(snapshot, enqueueError.Message, cancellationToken).ConfigureAwait(false);
        return Fin.Fail<Unit>(enqueueError);
    }

    private async ValueTask<Fin<Unit>> PersistSnapshotAsync(
        AgentRunSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var persistResult = await PersistVersionedSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (TryGetError(persistResult, out var persistError))
        {
            return Fin.Fail<Unit>(persistError);
        }

        var traceResult = await _traceWriter.WriteSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (TryGetError(traceResult, out var traceError))
        {
            return Fin.Fail<Unit>(traceError);
        }

        return UnitSuccess;
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

    private ValueTask<Fin<Unit>> EnqueueSnapshotAsync(
        AgentRunSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (_runQueue is null)
        {
            return new ValueTask<Fin<Unit>>(UnitSuccess);
        }

        var workItem = new AgentRunWorkItem(
            snapshot.RunId,
            snapshot.Objective,
            snapshot.MetadataOrEmpty,
            DateTimeOffset.UtcNow);

        return _runQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private async ValueTask MarkDispatchFailedAsync(
        AgentRunSnapshot queuedSnapshot,
        string? dispatchErrorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var runResult = RehydrateSnapshot(queuedSnapshot);
            if (TryGetError(runResult, out _))
            {
                return;
            }

            var run = UnsafeValue(runResult);
            var transitionResult = run.Fail(
                DateTimeOffset.UtcNow,
                $"AgentRouter run dispatch failed before worker processing: {dispatchErrorMessage}");

            if (!transitionResult.Succeeded)
            {
                return;
            }

            await PersistSnapshotAsync(run.ToSnapshot(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The caller already returns the original dispatch failure. This best-effort
            // transition prevents queue-rejected runs from staying queued when possible.
        }
    }

    private static Fin<AgentRun> RehydrateSnapshot(AgentRunSnapshot snapshot)
    {
        try
        {
            return Fin.Succ(AgentRun.Rehydrate(in snapshot));
        }
        catch (ArgumentException exception)
        {
            return Fin.Fail<AgentRun>(Error.New(exception.Message));
        }
    }

    private static Error? ValidateStartRequest(AgentRouterStartRunRequest request)
    {
        return request switch
        {
            { Objective.IsEmpty: true } => Error.New("agent objective is required."),
            _ => null
        };
    }

    private static Error? ValidateApproveRequest(AgentRouterApproveRunRequest request)
    {
        return request switch
        {
            { RunId.IsEmpty: true } => Error.New("agent run id is required."),
            { ApprovalId: var approvalId } when string.IsNullOrWhiteSpace(approvalId) =>
                Error.New("agent approval id is required."),
            _ => null
        };
    }

    private static IReadOnlyDictionary<string, string?> BuildApprovalMetadata(AgentRouterApproveRunRequest request)
    {
        var metadata = request.MetadataOrEmpty is { Count: > 0 } source
            ? new Dictionary<string, string?>(source, StringComparer.OrdinalIgnoreCase)
            : [];

        metadata[AgentRouterMetadataKeys.ApprovalGranted] = "true";
        metadata[AgentRouterMetadataKeys.ApprovalId] = request.ApprovalId.Trim();

        if (request.ApprovedBy is { } approvedBy && !string.IsNullOrWhiteSpace(approvedBy))
        {
            metadata[ApprovalApprovedByKey] = approvedBy.Trim();
        }

        return metadata;
    }

    private static AgentRun UnsafeValue(Fin<AgentRun> result)
    {
        return result.Match(
            Succ: static run => run,
            Fail: static error => throw new InvalidOperationException(error.Message));
    }

    private static bool TryGetError<T>(Fin<T> result, [NotNullWhen(true)] out Error? error)
    {
        var outcome = result.Match(
            Succ: static _ => (HasError: false, Error: default(Error)),
            Fail: static failure => (HasError: true, Error: failure));

        error = outcome.HasError ? outcome.Error : null;
        return outcome.HasError;
    }
}
