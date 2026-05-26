namespace MCPServer.AgentRouter.Application.Options;

public static class AgentRunQueueFullModes
{
    public const string Wait = "wait";
    public const string Reject = "reject";

    public static bool IsDefined(string? value)
    {
        return string.Equals(value, Wait, StringComparison.Ordinal)
            || string.Equals(value, Reject, StringComparison.Ordinal);
    }
}
