using Autofac;
using Microsoft.Extensions.Hosting;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Hosting.Routing;
using MCPServer.AgentRouter.Hosting.Services;

namespace MCPServer.AgentRouter.Hosting;

public sealed class AgentRouterHostingModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterInstance(AgentRouterBackgroundServiceOptions.Default)
            .AsSelf()
            .SingleInstance()
            .PreserveExistingDefaults();

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

        builder.RegisterType<AgentRouterBackgroundService>()
            .AsSelf()
            .As<IHostedService>()
            .As<IHostedLifecycleService>()
            .SingleInstance();
    }
}
