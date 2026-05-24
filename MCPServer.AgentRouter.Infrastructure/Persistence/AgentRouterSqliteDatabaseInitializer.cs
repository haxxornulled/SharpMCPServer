using MCPServer.AgentRouter.Infrastructure.Options;

namespace MCPServer.AgentRouter.Infrastructure.Persistence;

public sealed class AgentRouterSqliteDatabaseInitializer
{
    private readonly IAgentRouterSqliteDbContextFactory _dbContextFactory;
    private readonly AgentRouterSqliteOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _initialized;

    public AgentRouterSqliteDatabaseInitializer(
        IAgentRouterSqliteDbContextFactory dbContextFactory,
        AgentRouterSqliteOptions options)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_initialized || !_options.EnsureCreatedOnUse)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_initialized)
            {
                return;
            }

            await using var dbContext = _dbContextFactory.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
