using MCPServer.AgentRouter.Domain.Objectives;

namespace MCPServer.AgentRouter.Domain.Plans;

public sealed record AgentPlan(
    AgentObjective Objective,
    IReadOnlyList<AgentPlanStep> Steps,
    string? Summary);
