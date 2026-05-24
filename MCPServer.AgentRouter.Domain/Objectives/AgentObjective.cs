namespace MCPServer.AgentRouter.Domain.Objectives;

public readonly record struct AgentObjective(string Value)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    public static bool TryCreate(string? value, out AgentObjective objective)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            objective = default;
            return false;
        }

        objective = new AgentObjective(value.Trim());
        return true;
    }
}
