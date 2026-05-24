using LanguageExt;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Application.WorkItems;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentRunExecutor
{
    ValueTask<Fin<AgentRouterWorkerCycleResult>> ExecuteAsync(
        AgentRunWorkItem workItem,
        CancellationToken cancellationToken);
}
