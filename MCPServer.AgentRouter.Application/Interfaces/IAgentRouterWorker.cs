using LanguageExt;
using MCPServer.AgentRouter.Application.Models;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentRouterWorker
{
    ValueTask<Fin<AgentRouterWorkerCycleResult>> RunCycleAsync(
        CancellationToken cancellationToken);
}
