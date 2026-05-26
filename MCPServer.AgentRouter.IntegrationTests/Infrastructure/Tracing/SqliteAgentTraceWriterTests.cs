using Autofac;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Infrastructure.Options;
using MCPServer.AgentRouter.Infrastructure.Tracing;
using Xunit;

namespace MCPServer.AgentRouter.Infrastructure.Tests.Tracing;

public sealed class SqliteAgentTraceWriterTests
{
    [Fact]
    public void AgentRouterInfrastructureModule_Resolves_Sqlite_Trace_Writer_By_Default()
    {
        using var container = BuildContainer();

        var writer = container.Resolve<IAgentTraceWriter>();

        Assert.IsType<SqliteAgentTraceWriter>(writer);
    }

    [Fact]
    public async Task WriteSnapshotAsync_Appends_Snapshot_To_Sqlite_Trace()
    {
        using var container = BuildContainer();
        var writer = container.Resolve<IAgentTraceWriter>();
        var snapshot = CreateSnapshot();

        var result = await writer.WriteSnapshotAsync(snapshot, TestContext.Current.CancellationToken);

        Assert.True(result.Match(Succ: static _ => true, Fail: static _ => false));
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(new AgentRouterSqliteOptions
        {
            ConnectionString = $"Data Source={Path.Combine(Path.GetTempPath(), $"agent-router-trace-tests-{Guid.NewGuid():N}.db")};Cache=Shared"
        }).AsSelf().SingleInstance();
        builder.RegisterModule(new AgentRouterInfrastructureModule());
        return builder.Build();
    }

    private static AgentRunSnapshot CreateSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentRunSnapshot(
            AgentRunId.New(),
            CreateObjective("append sqlite trace snapshot"),
            AgentRunStatuses.Queued,
            now,
            now,
            "queued",
            Version: 0);
    }

    private static AgentObjective CreateObjective(string value)
    {
        Assert.True(AgentObjective.TryCreate(value, out var objective));
        return objective;
    }
}
