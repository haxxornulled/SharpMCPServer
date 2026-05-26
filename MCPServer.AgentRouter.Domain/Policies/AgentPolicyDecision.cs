namespace MCPServer.AgentRouter.Domain.Policies;

public readonly record struct AgentPolicyDecision(
    bool IsAllowed,
    bool RequiresApproval,
    string RiskLevel,
    string? Reason)
{
    public bool IsRejected => !IsAllowed && !RequiresApproval;

    public static AgentPolicyDecision Allowed(string riskLevel, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(riskLevel);

        return new AgentPolicyDecision(
            IsAllowed: true,
            RequiresApproval: false,
            RiskLevel: riskLevel.Trim(),
            Reason: reason);
    }

    public static AgentPolicyDecision AwaitingApproval(string riskLevel, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(riskLevel);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new AgentPolicyDecision(
            IsAllowed: false,
            RequiresApproval: true,
            RiskLevel: riskLevel.Trim(),
            Reason: reason.Trim());
    }

    public static AgentPolicyDecision Rejected(string riskLevel, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(riskLevel);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new AgentPolicyDecision(
            IsAllowed: false,
            RequiresApproval: false,
            RiskLevel: riskLevel.Trim(),
            Reason: reason.Trim());
    }
}
