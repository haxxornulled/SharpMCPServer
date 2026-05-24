using Autofac;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Defaults.Routing;
using MCPServer.AgentRouter.Defaults.Services;

namespace MCPServer.AgentRouter.Defaults;

public sealed class AgentRouterDefaultsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterType<NoOpAgentRouter>()
            .AsSelf()
            .As<IAgentRouter>()
            .SingleInstance();

        builder.RegisterType<DefaultAgentRouterRoute>()
            .As<IAgentRouterRoute>()
            .SingleInstance();

        builder.RegisterType<DefaultAgentRouterSelector>()
            .As<IAgentRouterSelector>()
            .SingleInstance();

        builder.RegisterType<DefaultAgentRouterProvider>()
            .As<IAgentRouterProvider>()
            .SingleInstance();
    }
}
