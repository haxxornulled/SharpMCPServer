using MCPServer.AgentRouter.Domain.Capabilities;
using MCPServer.AgentRouter.Domain.Policies;
using Xunit;

namespace MCPServer.AgentRouter.Domain.Tests.Policies;

public sealed class AgentCapabilityApprovalPolicyTests
{
    [Fact]
    public void Evaluate_Allows_Capability_That_Does_Not_Require_Approval()
    {
        var capability = AgentCapabilityDescriptor.Create(
            "safe-capability",
            "Safe capability",
            AgentExecutionRiskLevels.Low,
            requiresApproval: false);

        var decision = AgentCapabilityApprovalPolicy.Evaluate(capability, approvalGranted: false);

        Assert.True(decision.IsAllowed);
        Assert.False(decision.RequiresApproval);
        Assert.Equal(AgentExecutionRiskLevels.Low, decision.RiskLevel);
    }

    [Fact]
    public void Evaluate_Requires_Approval_For_High_Risk_Capability_When_Approval_Is_Missing()
    {
        var capability = AgentCapabilityDescriptor.Create(
            "remote-shell",
            "Remote shell",
            AgentExecutionRiskLevels.Critical,
            requiresApproval: true);

        var decision = AgentCapabilityApprovalPolicy.Evaluate(capability, approvalGranted: false);

        Assert.False(decision.IsAllowed);
        Assert.True(decision.RequiresApproval);
        Assert.False(decision.IsRejected);
        Assert.Equal(AgentExecutionRiskLevels.Critical, decision.RiskLevel);
        Assert.Contains("requires approval", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Allows_Approved_High_Risk_Capability()
    {
        var capability = AgentCapabilityDescriptor.Create(
            "remote-shell",
            "Remote shell",
            AgentExecutionRiskLevels.Critical,
            requiresApproval: true);

        var decision = AgentCapabilityApprovalPolicy.Evaluate(capability, approvalGranted: true);

        Assert.True(decision.IsAllowed);
        Assert.False(decision.RequiresApproval);
        Assert.Equal(AgentExecutionRiskLevels.Critical, decision.RiskLevel);
    }
}
