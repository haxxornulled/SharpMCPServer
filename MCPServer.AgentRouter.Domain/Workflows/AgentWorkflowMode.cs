namespace MCPServer.AgentRouter.Domain.Workflows;

public readonly record struct AgentWorkflowMode(string Value)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public bool IsDeterministic => string.Equals(Value, AgentWorkflowModes.Deterministic, StringComparison.Ordinal);

    public bool IsAgentic => string.Equals(Value, AgentWorkflowModes.Agentic, StringComparison.Ordinal);

    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    public static AgentWorkflowMode Deterministic => new(AgentWorkflowModes.Deterministic);

    public static AgentWorkflowMode Agentic => new(AgentWorkflowModes.Agentic);

    public static bool TryCreate(string? value, out AgentWorkflowMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = default;
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!string.Equals(normalized, AgentWorkflowModes.Deterministic, StringComparison.Ordinal)
            && !string.Equals(normalized, AgentWorkflowModes.Agentic, StringComparison.Ordinal))
        {
            mode = default;
            return false;
        }

        mode = new AgentWorkflowMode(normalized);
        return true;
    }
}
