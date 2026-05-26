using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;

namespace MCPServer.AgentRouter.Hosting.Routing;

public sealed class DefaultAgentRouterSelector : IAgentRouterSelector
{
    private readonly IAgentRouterRoute[] _routes;

    public DefaultAgentRouterSelector(IEnumerable<IAgentRouterRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var routeList = new List<IAgentRouterRoute>();
        foreach (var route in routes)
        {
            if (route is not null)
            {
                routeList.Add(route);
            }
        }

        var orderedRoutes = routeList.ToArray();
        Array.Sort(orderedRoutes, CompareRoutes);
        _routes = orderedRoutes;
    }

    public ValueTask<Fin<IAgentRouter>> SelectAsync(
        in AgentRouterProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var routes = _routes;
        for (var index = 0; index < routes.Length; index++)
        {
            var route = routes[index];
            if (route.IsMatch(in request))
            {
                return route.GetRouterAsync(in request, cancellationToken);
            }
        }

        return new ValueTask<Fin<IAgentRouter>>(
            Fin.Fail<IAgentRouter>(CreateNoRouteError(in request)));
    }

    private static int CompareRoutes(IAgentRouterRoute? left, IAgentRouterRoute? right)
    {
        return (left, right) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            ({ Order: var leftOrder }, { Order: var rightOrder }) when leftOrder != rightOrder => leftOrder.CompareTo(rightOrder),
            ({ Name: var leftName }, { Name: var rightName }) => string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static Error CreateNoRouteError(in AgentRouterProviderRequest request)
    {
        var routerName = request.RouterName;
        if (string.IsNullOrWhiteSpace(routerName))
        {
            return Error.New("No default agent router route is registered.");
        }

        return Error.New($"Agent router '{routerName}' is not registered.");
    }
}
