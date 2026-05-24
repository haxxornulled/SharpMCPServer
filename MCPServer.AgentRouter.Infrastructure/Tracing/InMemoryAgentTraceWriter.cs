using System.Collections.Concurrent;
using LanguageExt;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Infrastructure.Tracing;

public sealed class InMemoryAgentTraceWriter : IAgentTraceWriter
{
    private readonly ConcurrentQueue<AgentRunSnapshot> _snapshots = new();

    public IReadOnlyCollection<AgentRunSnapshot> Snapshots => _snapshots.ToArray();

    public ValueTask<Fin<Unit>> WriteSnapshotAsync(
        AgentRunSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _snapshots.Enqueue(snapshot);
        return new ValueTask<Fin<Unit>>(Fin.Succ(default(Unit)));
    }
}
