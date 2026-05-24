namespace MCPServer.AgentRouter.Domain.Runs;

public readonly record struct AgentRunTransitionResult(
    bool Succeeded,
    string? ErrorCode,
    string? Message)
{
    public static AgentRunTransitionResult Success()
    {
        return new AgentRunTransitionResult(
            Succeeded: true,
            ErrorCode: null,
            Message: null);
    }

    public static AgentRunTransitionResult Rejected(string errorCode, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new AgentRunTransitionResult(
            Succeeded: false,
            ErrorCode: errorCode,
            Message: message);
    }
}
