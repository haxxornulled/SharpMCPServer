using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Application.Models;

public readonly record struct AgentToolExecutionResult(
    AgentRunId RunId,
    bool Succeeded,
    string? Text,
    string? ErrorCode);
