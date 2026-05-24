using LanguageExt;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Plans;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentObjectivePlanner
{
    ValueTask<Fin<AgentPlan>> PlanAsync(
        AgentObjective objective,
        CancellationToken cancellationToken);
}
