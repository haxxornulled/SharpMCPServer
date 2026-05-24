namespace MCPServer.AgentRouter.Hosting;

public sealed class AgentRouterBackgroundServiceOptions
{
    public static AgentRouterBackgroundServiceOptions Default { get; } = new();

    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(1);

    public bool RunImmediately { get; init; } = true;
}
