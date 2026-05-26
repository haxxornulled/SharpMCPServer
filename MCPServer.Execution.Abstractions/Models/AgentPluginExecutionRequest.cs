using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.Execution.Abstractions.Models;

public readonly record struct AgentPluginExecutionRequest(
    AgentRunId RunId,
    AgentObjective Objective,
    string CapabilityName,
    IReadOnlyDictionary<string, string?>? Parameters)
{
    public IReadOnlyDictionary<string, string?> ParametersOrEmpty => Parameters ?? AgentRouterMetadata.Empty;
}
