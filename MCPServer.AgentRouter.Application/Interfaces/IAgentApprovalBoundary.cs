using MCPServer.AgentRouter.Domain.Policies;
using MCPServer.AgentRouter.Application.Models;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentApprovalBoundary
{
    AgentPolicyDecision Evaluate(AgentModelContext context);
}
