using System.Data.Common;
using MCPServer.AgentRouter.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MCPServer.AgentRouter.Infrastructure.Persistence;

public sealed class AgentRouterSqliteDatabaseInitializer
{
    private static readonly string[] SchemaStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS agent_runs (
            run_id TEXT NOT NULL CONSTRAINT pk_agent_runs PRIMARY KEY,
            objective TEXT NOT NULL,
            status TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            message TEXT NULL,
            version INTEGER NOT NULL,
            metadata_json TEXT NULL,
            lease_owner_id TEXT NULL,
            lease_acquired_at_utc TEXT NULL,
            lease_expires_at_utc TEXT NULL
        )
        """,
        """
        CREATE INDEX IF NOT EXISTS ix_agent_runs_status
        ON agent_runs (status)
        """,
        """
        CREATE INDEX IF NOT EXISTS ix_agent_runs_updated_at_utc
        ON agent_runs (updated_at_utc)
        """,
        """
        CREATE TABLE IF NOT EXISTS agent_run_traces (
            trace_id INTEGER NOT NULL CONSTRAINT pk_agent_run_traces PRIMARY KEY AUTOINCREMENT,
            run_id TEXT NOT NULL,
            objective TEXT NOT NULL,
            status TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            message TEXT NULL,
            version INTEGER NOT NULL,
            metadata_json TEXT NULL,
            lease_owner_id TEXT NULL,
            lease_acquired_at_utc TEXT NULL,
            lease_expires_at_utc TEXT NULL,
            written_at_utc TEXT NOT NULL
        )
        """,
        """
        CREATE INDEX IF NOT EXISTS ix_agent_run_traces_run_id
        ON agent_run_traces (run_id)
        """,
        """
        CREATE INDEX IF NOT EXISTS ix_agent_run_traces_written_at_utc
        ON agent_run_traces (written_at_utc)
        """
    ];

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
            await dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            foreach (var statement in SchemaStatements)
            {
                await dbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken).ConfigureAwait(false);
            }

            await EnsureNullableColumnAsync(
                    dbContext.Database.GetDbConnection(),
                    transaction.GetDbTransaction(),
                    tableName: "agent_runs",
                    columnName: "metadata_json",
                    definition: "metadata_json TEXT NULL",
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureNullableColumnAsync(
                    dbContext.Database.GetDbConnection(),
                    transaction.GetDbTransaction(),
                    tableName: "agent_runs",
                    columnName: "lease_owner_id",
                    definition: "lease_owner_id TEXT NULL",
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureNullableColumnAsync(
                    dbContext.Database.GetDbConnection(),
                    transaction.GetDbTransaction(),
                    tableName: "agent_runs",
                    columnName: "lease_acquired_at_utc",
                    definition: "lease_acquired_at_utc TEXT NULL",
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureNullableColumnAsync(
                    dbContext.Database.GetDbConnection(),
                    transaction.GetDbTransaction(),
                    tableName: "agent_runs",
                    columnName: "lease_expires_at_utc",
                    definition: "lease_expires_at_utc TEXT NULL",
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureNullableColumnAsync(
                    dbContext.Database.GetDbConnection(),
                    transaction.GetDbTransaction(),
                    tableName: "agent_run_traces",
                    columnName: "metadata_json",
                    definition: "metadata_json TEXT NULL",
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureNullableColumnAsync(
                    dbContext.Database.GetDbConnection(),
                    transaction.GetDbTransaction(),
                    tableName: "agent_run_traces",
                    columnName: "lease_owner_id",
                    definition: "lease_owner_id TEXT NULL",
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureNullableColumnAsync(
                    dbContext.Database.GetDbConnection(),
                    transaction.GetDbTransaction(),
                    tableName: "agent_run_traces",
                    columnName: "lease_acquired_at_utc",
                    definition: "lease_acquired_at_utc TEXT NULL",
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureNullableColumnAsync(
                    dbContext.Database.GetDbConnection(),
                    transaction.GetDbTransaction(),
                    tableName: "agent_run_traces",
                    columnName: "lease_expires_at_utc",
                    definition: "lease_expires_at_utc TEXT NULL",
                    cancellationToken)
                .ConfigureAwait(false);

            await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    CREATE INDEX IF NOT EXISTS ix_agent_runs_lease_expires_at_utc
                    ON agent_runs (lease_expires_at_utc)
                    """,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async ValueTask EnsureNullableColumnAsync(
        DbConnection connection,
        DbTransaction transaction,
        string tableName,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(connection, transaction, tableName, columnName, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {definition}";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> ColumnExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info('{tableName}')";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader["name"] is string currentColumnName
                && string.Equals(currentColumnName, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
