using MCPServer.AgentRouter.Domain.Capabilities;
using MCPServer.Execution.Abstractions.Models;

namespace MCPServer.AgentRouter.Application.Models;

public readonly record struct AgentPluginPolicyRequest(
    AgentPluginExecutionRequest ExecutionRequest,
    AgentCapabilityDescriptor Capability);
