using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MCPServer.AgentRouter.Infrastructure.Stores;

public sealed class SqliteAgentRunStore : IAgentRunStore
{
    private readonly IAgentRouterSqliteDbContextFactory _dbContextFactory;
    private readonly AgentRouterSqliteDatabaseInitializer _databaseInitializer;

    public SqliteAgentRunStore(
        IAgentRouterSqliteDbContextFactory dbContextFactory,
        AgentRouterSqliteDatabaseInitializer databaseInitializer)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _databaseInitializer = databaseInitializer ?? throw new ArgumentNullException(nameof(databaseInitializer));
    }

    public async ValueTask<Fin<Unit>> TryCreateAsync(
        AgentRunSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateSnapshot(snapshot) is { } validationError)
        {
            return Fail(validationError);
        }

        if (snapshot.Version is not 0)
        {
            return Fail(Error.New(
                $"agent run '{snapshot.RunId.Value}' create expected version 0 but received version {snapshot.Version}."));
        }

        try
        {
            await _databaseInitializer.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var dbContext = _dbContextFactory.CreateDbContext();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            dbContext.AgentRuns.Add(AgentRunRecord.FromSnapshot(snapshot));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Succeed();
        }
        catch (DbUpdateException exception)
        {
            return Fail(Error.New($"agent run '{snapshot.RunId.Value}' could not be created. {exception.GetBaseException().Message}"));
        }
        catch (InvalidOperationException exception)
        {
            return Fail(Error.New(exception.Message));
        }
    }

    public async ValueTask<Fin<Unit>> TryUpdateAsync(
        AgentRunSnapshot snapshot,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateSnapshot(snapshot) is { } validationError)
        {
            return Fail(validationError);
        }

        if (expectedVersion < 0)
        {
            return Fail(Error.New("agent run expected version cannot be negative."));
        }

        if (snapshot.Version != expectedVersion + 1)
        {
            return Fail(Error.New(
                $"agent run '{snapshot.RunId.Value}' update version {snapshot.Version} does not follow expected version {expectedVersion}."));
        }

        try
        {
            await _databaseInitializer.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var dbContext = _dbContextFactory.CreateDbContext();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var metadataJson = AgentRunSnapshotJson.SerializeMetadata(snapshot.Metadata);
            var rows = await dbContext.AgentRuns
                .Where(run => run.RunId == snapshot.RunId.Value && run.Version == expectedVersion)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Objective, snapshot.Objective.Value)
                    .SetProperty(run => run.Status, snapshot.Status)
                    .SetProperty(run => run.CreatedAtUtc, snapshot.CreatedAtUtc)
                    .SetProperty(run => run.UpdatedAtUtc, snapshot.UpdatedAtUtc)
                    .SetProperty(run => run.Message, snapshot.Message)
                    .SetProperty(run => run.Version, snapshot.Version)
                    .SetProperty(run => run.MetadataJson, metadataJson),
                    cancellationToken)
                .ConfigureAwait(false);

            if (rows == 1)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Succeed();
            }

            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return await BuildUpdateConflictAsync(dbContext, snapshot.RunId, expectedVersion, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return Fail(Error.New(exception.Message));
        }
    }

    public async ValueTask<Fin<AgentRunSnapshot>> GetSnapshotAsync(
        AgentRunId runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (runId.IsEmpty)
        {
            return Fin.Fail<AgentRunSnapshot>(Error.New("agent run id is required."));
        }

        try
        {
            await _databaseInitializer.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var dbContext = _dbContextFactory.CreateDbContext();

            var record = await dbContext.AgentRuns
                .AsNoTracking()
                .SingleOrDefaultAsync(run => run.RunId == runId.Value, cancellationToken)
                .ConfigureAwait(false);

            return record is null
                ? Fin.Fail<AgentRunSnapshot>(Error.New($"agent run '{runId.Value}' was not found."))
                : Fin.Succ(record.ToSnapshot());
        }
        catch (InvalidOperationException exception)
        {
            return Fin.Fail<AgentRunSnapshot>(Error.New(exception.Message));
        }
        catch (JsonException exception)
        {
            return Fin.Fail<AgentRunSnapshot>(Error.New(exception.Message));
        }
    }

    private static async ValueTask<Fin<Unit>> BuildUpdateConflictAsync(
        AgentRouterSqliteDbContext dbContext,
        AgentRunId runId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var currentVersion = await dbContext.AgentRuns
            .AsNoTracking()
            .Where(run => run.RunId == runId.Value)
            .Select(run => (long?)run.Version)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return currentVersion switch
        {
            null => Fail(Error.New($"agent run '{runId.Value}' was not found.")),
            _ => Fail(Error.New(
                $"agent run '{runId.Value}' concurrency conflict. Expected version {expectedVersion}, actual version {currentVersion.Value}."))
        };
    }

    private static Error? ValidateSnapshot(AgentRunSnapshot snapshot)
    {
        return snapshot switch
        {
            { RunId.IsEmpty: true } => Error.New("agent run id is required."),
            { Objective.IsEmpty: true } => Error.New("agent objective is required."),
            { Version: < 0 } => Error.New("agent run version cannot be negative."),
            _ when string.IsNullOrWhiteSpace(snapshot.Status) => Error.New("agent run status is required."),
            _ => null
        };
    }

    private static Fin<Unit> Succeed() => Fin.Succ(default(Unit));

    private static Fin<Unit> Fail(Error error) => Fin.Fail<Unit>(error);
}
