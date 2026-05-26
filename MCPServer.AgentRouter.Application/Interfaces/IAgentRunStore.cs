using LanguageExt;
using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentRunStore
{
    ValueTask<Fin<Unit>> TryCreateAsync(
        AgentRunSnapshot snapshot,
        CancellationToken cancellationToken);

    ValueTask<Fin<Unit>> TryUpdateAsync(
        AgentRunSnapshot snapshot,
        long expectedVersion,
        CancellationToken cancellationToken);

    ValueTask<Fin<AgentRunSnapshot>> GetSnapshotAsync(
        AgentRunId runId,
        CancellationToken cancellationToken);
}
