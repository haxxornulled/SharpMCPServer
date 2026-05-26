namespace MCPServer.AgentRouter.Domain.Runs;

public readonly record struct AgentRunId(string Value)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    public static AgentRunId New()
    {
        return new AgentRunId($"agent-run-{Guid.NewGuid():N}");
    }
}
