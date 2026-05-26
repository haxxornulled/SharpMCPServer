using MCPServer.AgentRouter.Domain.Capabilities;

namespace MCPServer.AgentRouter.Domain.Policies;

public static class AgentCapabilityApprovalPolicy
{
    public static AgentPolicyDecision Evaluate(
        AgentCapabilityDescriptor capability,
        bool approvalGranted)
    {
        ArgumentNullException.ThrowIfNull(capability);

        if (!capability.RequiresApproval)
        {
            return AgentPolicyDecision.Allowed(
                capability.RiskLevel,
                $"Capability '{capability.Name.Value}' does not require approval.");
        }

        if (approvalGranted)
        {
            return AgentPolicyDecision.Allowed(
                capability.RiskLevel,
                $"Capability '{capability.Name.Value}' was explicitly approved.");
        }

        return AgentPolicyDecision.AwaitingApproval(
            capability.RiskLevel,
            $"Capability '{capability.Name.Value}' requires approval before execution.");
    }
}
