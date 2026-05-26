using LanguageExt;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Hosting.Services;

namespace MCPServer.AgentRouter.Hosting.Routing;

public sealed class PlanningAgentRouterRoute : IAgentRouterRoute
{
    private readonly PlanningAgentRouter _router;

    public PlanningAgentRouterRoute(PlanningAgentRouter router)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    public string Name => AgentRouterNames.Planning;

    public int Order => -10;

    public bool IsMatch(in AgentRouterProviderRequest request)
    {
        return string.Equals(request.RouterName, AgentRouterNames.Planning, StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<Fin<IAgentRouter>> GetRouterAsync(
        in AgentRouterProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<Fin<IAgentRouter>>(Fin.Succ<IAgentRouter>(_router));
    }
}
