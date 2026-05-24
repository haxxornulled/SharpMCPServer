using Autofac;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Infrastructure.Options;
using MCPServer.AgentRouter.Infrastructure.Persistence;
using MCPServer.AgentRouter.Infrastructure.Queues;
using MCPServer.AgentRouter.Infrastructure.Stores;
using MCPServer.AgentRouter.Infrastructure.Tracing;

namespace MCPServer.AgentRouter.Infrastructure;

public sealed class AgentRouterInfrastructureModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterInstance(AgentRouterSqliteOptions.Default)
            .AsSelf()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<BoundedChannelAgentRunQueue>()
            .AsSelf()
            .As<IAgentRunQueue>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<AgentRouterSqliteDbContextFactory>()
            .As<IAgentRouterSqliteDbContextFactory>()
            .SingleInstance();

        builder.RegisterType<AgentRouterSqliteDatabaseInitializer>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<InMemoryAgentRunStore>()
            .AsSelf()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<InMemoryAgentTraceWriter>()
            .AsSelf()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<SqliteAgentRunStore>()
            .AsSelf()
            .As<IAgentRunStore>()
            .SingleInstance();

        builder.RegisterType<SqliteAgentTraceWriter>()
            .AsSelf()
            .As<IAgentTraceWriter>()
            .SingleInstance();
    }
}
