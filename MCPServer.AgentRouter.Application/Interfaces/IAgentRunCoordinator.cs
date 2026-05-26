using LanguageExt;
using MCPServer.Execution.Abstractions.Models;
using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentRunCoordinator
{
    ValueTask<Fin<AgentRouterStartRunResult>> StartAsync(
        in AgentRouterStartRunRequest request,
        CancellationToken cancellationToken);

    ValueTask<Fin<AgentRunSnapshot>> GetSnapshotAsync(
        AgentRunId runId,
        CancellationToken cancellationToken);

    ValueTask<Fin<AgentRunSnapshot>> ApproveAsync(
        in AgentRouterApproveRunRequest request,
        CancellationToken cancellationToken);

    ValueTask<Fin<Unit>> CancelAsync(
        AgentRunId runId,
        CancellationToken cancellationToken);
}
