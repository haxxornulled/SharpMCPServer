using MCPServer.AgentRouter.Domain.Capabilities;

namespace MCPServer.AgentRouter.Application.Models;

public readonly record struct AgentPluginPolicyRequest(
    AgentPluginExecutionRequest ExecutionRequest,
    AgentCapabilityDescriptor Capability);
