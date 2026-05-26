using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Domain.Policies;

namespace MCPServer.AgentRouter.Application.Services;

public sealed class DefaultAgentApprovalBoundary : IAgentApprovalBoundary
{
    public AgentPolicyDecision Evaluate(AgentModelContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Target.RequiresApproval)
        {
            return AgentPolicyDecision.Allowed(
                context.Target.RiskLevel,
                $"Target '{context.Target.Name}' stays within the host boundary.");
        }

        if (context.ApprovalGranted)
        {
            return AgentPolicyDecision.Allowed(
                context.Target.RiskLevel,
                $"Approval token granted for target '{context.Target.Name}'.");
        }

        return AgentPolicyDecision.AwaitingApproval(
            context.Target.RiskLevel,
            $"Approval token required before target '{context.Target.Name}' can touch external systems.");
    }
}
