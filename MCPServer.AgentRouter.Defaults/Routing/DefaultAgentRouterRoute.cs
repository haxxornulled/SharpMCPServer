using LanguageExt;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Defaults.Services;

namespace MCPServer.AgentRouter.Defaults.Routing;

public sealed class DefaultAgentRouterRoute : IAgentRouterRoute
{
    private readonly NoOpAgentRouter _router;

    public DefaultAgentRouterRoute(NoOpAgentRouter router)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    public string Name => AgentRouterNames.Default;

    public int Order => 0;

    public bool IsMatch(in AgentRouterProviderRequest request)
    {
        var routerName = request.RouterName;

        return string.IsNullOrWhiteSpace(routerName) ||
            string.Equals(routerName, AgentRouterNames.Default, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(routerName, AgentRouterNames.NoOp, StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<Fin<IAgentRouter>> GetRouterAsync(
        in AgentRouterProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<Fin<IAgentRouter>>(Fin.Succ<IAgentRouter>(_router));
    }
}
