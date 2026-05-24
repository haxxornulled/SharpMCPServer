using MCPServer.AgentRouter.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;

namespace MCPServer.AgentRouter.Infrastructure.Persistence;

public sealed class AgentRouterSqliteDbContextFactory : IAgentRouterSqliteDbContextFactory
{
    private readonly AgentRouterSqliteOptions _options;

    public AgentRouterSqliteDbContextFactory(AgentRouterSqliteOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public AgentRouterSqliteDbContext CreateDbContext()
    {
        var builder = new DbContextOptionsBuilder<AgentRouterSqliteDbContext>()
            .UseSqlite(_options.ConnectionString);

        return new AgentRouterSqliteDbContext(builder.Options);
    }
}
