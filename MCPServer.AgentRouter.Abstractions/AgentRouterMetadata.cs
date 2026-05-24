namespace MCPServer.AgentRouter.Abstractions;

public static class AgentRouterMetadata
{
    public static IReadOnlyDictionary<string, string?> Empty { get; } = new Dictionary<string, string?>(capacity: 0);
}
