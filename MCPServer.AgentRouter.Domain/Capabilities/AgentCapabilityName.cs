namespace MCPServer.AgentRouter.Domain.Capabilities;

public readonly record struct AgentCapabilityName(string Value)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    public static bool TryCreate(string? value, out AgentCapabilityName name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            name = default;
            return false;
        }

        name = new AgentCapabilityName(value.Trim());
        return true;
    }
}
