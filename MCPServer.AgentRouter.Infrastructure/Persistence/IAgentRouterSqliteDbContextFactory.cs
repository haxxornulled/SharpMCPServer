namespace MCPServer.AgentRouter.Infrastructure.Persistence;

public interface IAgentRouterSqliteDbContextFactory
{
    AgentRouterSqliteDbContext CreateDbContext();
}
