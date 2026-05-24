using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Application.Models;

public readonly record struct AgentToolExecutionRequest(
    AgentRunId RunId,
    string CapabilityName,
    string ToolName,
    IReadOnlyDictionary<string, string?> Arguments);
