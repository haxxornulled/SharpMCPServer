using Autofac;
using Microsoft.Extensions.Hosting;
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

        builder.RegisterType<AgentRouterBackgroundService>()
            .AsSelf()
            .As<IHostedService>()
            .As<IHostedLifecycleService>()
            .SingleInstance();
    }
}
