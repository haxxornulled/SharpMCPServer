namespace MCPServer.AgentRouter.Domain.Plans;

public sealed record AgentPlanStep(
    int Order,
    string Name,
    string CapabilityName,
    string? Description);
