using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MCPServer.AgentRouter.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentRouterInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services;
    }
}
