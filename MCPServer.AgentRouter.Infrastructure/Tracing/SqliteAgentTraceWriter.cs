using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MCPServer.AgentRouter.Infrastructure.Tracing;

public sealed class SqliteAgentTraceWriter : IAgentTraceWriter
{
    private readonly IAgentRouterSqliteDbContextFactory _dbContextFactory;
    private readonly AgentRouterSqliteDatabaseInitializer _databaseInitializer;

    public SqliteAgentTraceWriter(
        IAgentRouterSqliteDbContextFactory dbContextFactory,
        AgentRouterSqliteDatabaseInitializer databaseInitializer)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _databaseInitializer = databaseInitializer ?? throw new ArgumentNullException(nameof(databaseInitializer));
    }

    public async ValueTask<Fin<Unit>> WriteSnapshotAsync(
        AgentRunSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateSnapshot(snapshot) is { } validationError)
        {
            return Fin.Fail<Unit>(validationError);
        }

        try
        {
            await _databaseInitializer.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var dbContext = _dbContextFactory.CreateDbContext();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            dbContext.AgentRunTraces.Add(AgentRunTraceRecord.FromSnapshot(snapshot, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Fin.Succ(default(Unit));
        }
        catch (DbUpdateException exception)
        {
            return Fin.Fail<Unit>(Error.New(exception.GetBaseException().Message));
        }
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
}
