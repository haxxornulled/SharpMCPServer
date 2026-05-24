using MCPServer.AgentRouter.Domain.Policies;

namespace MCPServer.AgentRouter.Domain.Capabilities;

public sealed record AgentCapabilityDescriptor(
    AgentCapabilityName Name,
    string DisplayName,
    string RiskLevel,
    bool RequiresApproval,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public bool IsCritical => string.Equals(RiskLevel, AgentExecutionRiskLevels.Critical, StringComparison.OrdinalIgnoreCase);

    public static AgentCapabilityDescriptor Create(
        string name,
        string displayName,
        string riskLevel,
        bool requiresApproval,
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        if (!AgentCapabilityName.TryCreate(name, out var capabilityName))
        {
            throw new ArgumentException("Capability name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Capability display name is required.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(riskLevel))
        {
            throw new ArgumentException("Capability risk level is required.", nameof(riskLevel));
        }

        return new AgentCapabilityDescriptor(
            capabilityName,
            displayName.Trim(),
            riskLevel.Trim(),
            requiresApproval,
            metadata ?? new Dictionary<string, string?>(capacity: 0));
    }
}
