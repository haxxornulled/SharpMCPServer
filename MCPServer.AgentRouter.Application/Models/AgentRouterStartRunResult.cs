using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Application.Models;

public readonly record struct AgentRouterStartRunResult(
    AgentRunId RunId,
    string Status,
    string? Message);
