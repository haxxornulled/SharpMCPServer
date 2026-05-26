using Autofac;
using MCPServer.AgentRouter.Abstractions.Interfaces;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Options;
using MCPServer.AgentRouter.Application.Services;
using MCPServer.Execution.Abstractions;

namespace MCPServer.AgentRouter.Application;

public sealed class AgentRouterApplicationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterInstance(AgentRouterConcurrencyOptions.Default)
            .AsSelf()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<DefaultAgentRunCoordinator>()
            .As<IAgentRunCoordinator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<DefaultAgentObjectivePlanner>()
            .As<IAgentObjectivePlanner>()
            .SingleInstance();

        builder.RegisterType<DefaultAgentModelContextBuilder>()
            .As<IAgentModelContextBuilder>()
            .SingleInstance();

        builder.RegisterType<DefaultAgentApprovalBoundary>()
            .As<IAgentApprovalBoundary>()
            .SingleInstance();

        builder.RegisterType<DefaultAgentRoutingPlanner>()
            .As<IAgentRoutingPlanner>()
            .SingleInstance();

        builder.RegisterType<AgentRouterBridgeFacade>()
            .As<IAgentRouterBridgeFacade>()
            .SingleInstance();

        builder.RegisterType<DefaultAgentRunExecutor>()
            .As<IAgentRunExecutor>()
            .SingleInstance();

        builder.RegisterType<DefaultAgentRouterWorker>()
            .As<IAgentRouterWorker>()
            .SingleInstance();

        builder.RegisterType<DefaultAgentPluginRegistry>()
            .As<IAgentPluginRegistry>()
            .SingleInstance();

        builder.RegisterType<DefaultAgentPluginPolicyEvaluator>()
            .As<IAgentPluginPolicyEvaluator>()
            .SingleInstance();
    }
}
