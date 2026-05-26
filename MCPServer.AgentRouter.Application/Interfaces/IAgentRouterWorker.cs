using LanguageExt;
using MCPServer.Execution.Abstractions.Models;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentRouterWorker
{
    ValueTask<Fin<AgentRouterWorkerCycleResult>> RunCycleAsync(
        CancellationToken cancellationToken);
}
