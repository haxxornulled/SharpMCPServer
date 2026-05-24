using System.Collections.Concurrent;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Infrastructure.Stores;

public sealed class InMemoryAgentRunStore : IAgentRunStore
{
    private readonly ConcurrentDictionary<string, AgentRunSnapshot> _snapshots = new(StringComparer.Ordinal);

    public ValueTask<Fin<Unit>> TryCreateAsync(
        AgentRunSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateSnapshot(snapshot) is { } validationError)
        {
            return Failure(validationError);
        }

        if (snapshot.Version is not 0)
        {
            return Failure(Error.New(
                $"agent run '{snapshot.RunId.Value}' create expected version 0 but received version {snapshot.Version}."));
        }

        var created = _snapshots.TryAdd(snapshot.RunId.Value, snapshot);
        return created
            ? Success()
            : Failure(Error.New($"agent run '{snapshot.RunId.Value}' already exists."));
    }

    public ValueTask<Fin<Unit>> TryUpdateAsync(
        AgentRunSnapshot snapshot,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateSnapshot(snapshot) is { } validationError)
        {
            return Failure(validationError);
        }

        if (expectedVersion < 0)
        {
            return Failure(Error.New("agent run expected version cannot be negative."));
        }

        if (snapshot.Version != expectedVersion + 1)
        {
            return Failure(Error.New(
                $"agent run '{snapshot.RunId.Value}' update version {snapshot.Version} does not follow expected version {expectedVersion}."));
        }

        while (true)
        {
            if (!_snapshots.TryGetValue(snapshot.RunId.Value, out var current))
            {
                return Failure(Error.New($"agent run '{snapshot.RunId.Value}' was not found."));
            }

            if (current.Version != expectedVersion)
            {
                return Failure(Error.New(
                    $"agent run '{snapshot.RunId.Value}' concurrency conflict. Expected version {expectedVersion}, actual version {current.Version}."));
            }

            if (_snapshots.TryUpdate(snapshot.RunId.Value, snapshot, current))
            {
                return Success();
            }
        }
    }

    public ValueTask<Fin<AgentRunSnapshot>> GetSnapshotAsync(
        AgentRunId runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return runId switch
        {
            { IsEmpty: true } => new ValueTask<Fin<AgentRunSnapshot>>(
                Fin.Fail<AgentRunSnapshot>(Error.New("agent run id is required."))),

            _ when _snapshots.TryGetValue(runId.Value, out var snapshot) =>
                new ValueTask<Fin<AgentRunSnapshot>>(Fin.Succ(snapshot)),

            _ => new ValueTask<Fin<AgentRunSnapshot>>(
                Fin.Fail<AgentRunSnapshot>(Error.New($"agent run '{runId.Value}' was not found.")))
        };
    }

    private static Error? ValidateSnapshot(AgentRunSnapshot snapshot)
    {
        return snapshot switch
        {
            { RunId.IsEmpty: true } => Error.New("agent run id is required."),
            { Objective.IsEmpty: true } => Error.New("agent objective is required."),
            { Version: < 0 } => Error.New("agent run version cannot be negative."),
            _ when string.IsNullOrWhiteSpace(snapshot.Status) => Error.New("agent run status is required."),
            _ => null
        };
    }

    private static ValueTask<Fin<Unit>> Success()
    {
        return new ValueTask<Fin<Unit>>(Fin.Succ(default(Unit)));
    }

    private static ValueTask<Fin<Unit>> Failure(Error error)
    {
        return new ValueTask<Fin<Unit>>(Fin.Fail<Unit>(error));
    }
}
