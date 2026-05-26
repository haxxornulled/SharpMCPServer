using LanguageExt;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Application.Models;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentRoutingPlanner
{
    ValueTask<Fin<AgentRoutingDecision>> PlanAsync(
        in AgentRouterRunRequest request,
        CancellationToken cancellationToken);
}
