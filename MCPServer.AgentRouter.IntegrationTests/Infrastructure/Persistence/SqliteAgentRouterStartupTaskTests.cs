using Autofac;
using LanguageExt;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Infrastructure.Options;
using MCPServer.AgentRouter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MCPServer.AgentRouter.Infrastructure.Tests.Persistence;

public sealed class SqliteAgentRouterStartupTaskTests
{
    [Fact]
    public void AgentRouterInfrastructureModule_Registers_Sqlite_Startup_Task()
    {
        using var container = BuildContainer();

        var startupTasks = container.Resolve<IEnumerable<IAgentRouterStartupTask>>().ToArray();

        Assert.Contains(startupTasks, static task => task is SqliteAgentRouterStartupTask);
    }

    [Fact]
    public async Task ExecuteAsync_Initializes_Sqlite_Database()
    {
        using var container = BuildContainer();
        var startupTask = container.Resolve<IEnumerable<IAgentRouterStartupTask>>()
            .OfType<SqliteAgentRouterStartupTask>()
            .Single();
        var dbContextFactory = container.Resolve<IAgentRouterSqliteDbContextFactory>();

        var result = await startupTask.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.True(IsSuccess(result), FailureMessage(result));

        await using var dbContext = dbContextFactory.CreateDbContext();
        Assert.True(await dbContext.Database.CanConnectAsync(TestContext.Current.CancellationToken));
    }


    [Fact]
    public async Task ExecuteAsync_Adds_Metadata_Columns_To_Legacy_Schema()
    {
        using var container = BuildContainer();
        var startupTask = container.Resolve<IEnumerable<IAgentRouterStartupTask>>()
            .OfType<SqliteAgentRouterStartupTask>()
            .Single();
        var dbContextFactory = container.Resolve<IAgentRouterSqliteDbContextFactory>();
        var store = container.Resolve<IAgentRunStore>();

        await CreateLegacySchemaAsync(dbContextFactory, TestContext.Current.CancellationToken);

        var startupResult = await startupTask.ExecuteAsync(TestContext.Current.CancellationToken);
        Assert.True(IsSuccess(startupResult), FailureMessage(startupResult));

        var snapshot = CreateSnapshot(new Dictionary<string, string?>
        {
            ["agent.capability"] = "legacy-schema-check"
        });

        var createResult = await store.TryCreateAsync(snapshot, TestContext.Current.CancellationToken);
        var loaded = await store.GetSnapshotAsync(snapshot.RunId, TestContext.Current.CancellationToken);

        Assert.True(IsSuccess(createResult), FailureMessage(createResult));
        Assert.Equal("legacy-schema-check", UnsafeValue(loaded).MetadataOrEmpty["agent.capability"]);
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(new AgentRouterSqliteOptions
        {
            ConnectionString = $"Data Source={Path.Combine(Path.GetTempPath(), $"agent-router-startup-{Guid.NewGuid():N}.db")};Cache=Shared"
        }).AsSelf().SingleInstance();
        builder.RegisterModule(new AgentRouterInfrastructureModule());
        return builder.Build();
    }

    private static bool IsSuccess<T>(Fin<T> result)
    {
        return result.Match(Succ: static _ => true, Fail: static _ => false);
    }


    private static async Task CreateLegacySchemaAsync(
        IAgentRouterSqliteDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = dbContextFactory.CreateDbContext();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE agent_runs (
                run_id TEXT NOT NULL CONSTRAINT pk_agent_runs PRIMARY KEY,
                objective TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                message TEXT NULL,
                version INTEGER NOT NULL
            )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE agent_run_traces (
                trace_id INTEGER NOT NULL CONSTRAINT pk_agent_run_traces PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                objective TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                message TEXT NULL,
                version INTEGER NOT NULL,
                written_at_utc TEXT NOT NULL
            )
            """,
            cancellationToken);
    }

    private static AgentRunSnapshot CreateSnapshot(IReadOnlyDictionary<string, string?>? metadata = null)
    {
        Assert.True(AgentObjective.TryCreate("persist agent run after legacy schema upgrade", out var objective));
        var now = DateTimeOffset.UtcNow;
        return new AgentRunSnapshot(
            AgentRunId.New(),
            objective,
            AgentRunStatuses.Queued,
            now,
            now,
            "queued",
            0,
            metadata);
    }

    private static T UnsafeValue<T>(Fin<T> result)
    {
        return result.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));
    }

    private static string FailureMessage<T>(Fin<T> result)
    {
        return result.Match(
            Succ: static _ => string.Empty,
            Fail: static error => error.Message);
    }
}
