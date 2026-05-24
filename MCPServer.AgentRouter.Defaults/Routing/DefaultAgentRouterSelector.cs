using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;

namespace MCPServer.AgentRouter.Defaults.Routing;

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
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        var orderComparison = left.Order.CompareTo(right.Order);
        if (orderComparison != 0)
        {
            return orderComparison;
        }

        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
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
