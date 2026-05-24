using Microsoft.Extensions.DependencyInjection;

namespace MCPServer.AgentRouter.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentRouterApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
