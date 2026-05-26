using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Application.Interfaces;

namespace MCPServer.AgentRouter.Infrastructure.Persistence;

public sealed class SqliteAgentRouterStartupTask : IAgentRouterStartupTask
{
    private readonly AgentRouterSqliteDatabaseInitializer _databaseInitializer;

    public SqliteAgentRouterStartupTask(AgentRouterSqliteDatabaseInitializer databaseInitializer)
    {
        _databaseInitializer = databaseInitializer ?? throw new ArgumentNullException(nameof(databaseInitializer));
    }

    public string Name => "sqlite-database-initializer";

    public async ValueTask<Fin<Unit>> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _databaseInitializer.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            return Fin.Succ(default(Unit));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Fin.Fail<Unit>(Error.New(exception.Message));
        }
    }
}
