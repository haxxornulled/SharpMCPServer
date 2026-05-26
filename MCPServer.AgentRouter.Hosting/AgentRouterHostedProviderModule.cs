using Autofac;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Application;
using MCPServer.AgentRouter.Infrastructure;
using MCPServer.AgentRouter.Hosting.Routing;
using MCPServer.AgentRouter.Hosting.Services;

namespace MCPServer.AgentRouter.Hosting;

public sealed class AgentRouterHostedProviderModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterModule(new AgentRouterApplicationModule());
        builder.RegisterModule(new AgentRouterInfrastructureModule());
        builder.RegisterModule(new AgentRouterHostingModule());

        builder.RegisterType<PlanningAgentRouter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<PlanningAgentRouterRoute>()
            .As<IAgentRouterRoute>()
            .SingleInstance();
    }
}
