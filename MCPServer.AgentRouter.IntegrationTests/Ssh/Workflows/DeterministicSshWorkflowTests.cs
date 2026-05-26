using Autofac;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Application;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.Execution.Abstractions.Models;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Domain.Workflows;
using MCPServer.AgentRouter.Infrastructure;
using MCPServer.AgentRouter.Infrastructure.Options;
using MCPServer.ExecutionPlugins.Ssh;
using MCPServer.ExecutionPlugins.Ssh.Tests.Testing;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Models;
using Microsoft.Data.Sqlite;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPServer.AgentRouter.IntegrationTests.Ssh.Workflows;

public sealed class DeterministicSshWorkflowTests
{
    [Fact]
    public async Task Deterministic_RemoteShell_Run_Waits_For_Approval_Then_Executes_Through_Ssh_Plugin()
    {
        await using var database = TempSqliteDatabase.Create();
        using var container = BuildContainer(database.ConnectionString);

        var coordinator = container.Resolve<IAgentRunCoordinator>();
        var worker = container.Resolve<IAgentRouterWorker>();
        var runStore = container.Resolve<IAgentRunStore>();
        var sshRuntime = container.Resolve<FakeSshAgentRuntime>();
        var objective = CreateObjective("deterministic SSH whoami workflow");
        var startRequest = new AgentRouterStartRunRequest(
            Objective: objective,
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.WorkflowMode] = AgentWorkflowModes.Deterministic,
                [AgentRouterMetadataKeys.Capability] = AgentRouterSshCapabilityNames.RemoteShell,
                [AgentRouterSshMetadataKeys.Profile] = "debian-root-lab",
                [AgentRouterSshMetadataKeys.Command] = "whoami",
                [AgentRouterSshMetadataKeys.ArgumentsJson] = "[]"
            });

        var start = TestFin.Success(await coordinator.StartAsync(in startRequest, TestContext.Current.CancellationToken));
        var firstCycle = TestFin.Success(await worker.RunCycleAsync(TestContext.Current.CancellationToken));
        var awaitingApproval = TestFin.Success(await runStore.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(1, firstCycle.ProcessedCount);
        Assert.Equal(AgentRunStatuses.AwaitingApproval, awaitingApproval.Status);
        Assert.Null(sshRuntime.LastLaunchRequest);

        var approvalRequest = new AgentRouterApproveRunRequest(
            RunId: start.RunId,
            ApprovalId: "approval-ssh-whoami-001",
            ApprovedBy: "integration-test",
            Metadata: null);

        var approvedSnapshot = TestFin.Success(await coordinator.ApproveAsync(in approvalRequest, TestContext.Current.CancellationToken));
        Assert.Equal(AgentRunStatuses.Queued, approvedSnapshot.Status);
        Assert.NotNull(approvedSnapshot.Metadata);
        var approvedMetadata = approvedSnapshot.Metadata;
        Assert.Equal("true", approvedMetadata[AgentRouterMetadataKeys.ApprovalGranted]);
        Assert.Equal("approval-ssh-whoami-001", approvedMetadata[AgentRouterMetadataKeys.ApprovalId]);
        Assert.Equal(AgentRouterSshCapabilityNames.RemoteShell, approvedMetadata[AgentRouterMetadataKeys.Capability]);

        var secondCycle = TestFin.Success(await worker.RunCycleAsync(TestContext.Current.CancellationToken));
        var completed = TestFin.Success(await runStore.GetSnapshotAsync(start.RunId, TestContext.Current.CancellationToken));

        Assert.Equal(1, secondCycle.ProcessedCount);
        Assert.Equal(AgentRunStatuses.Completed, completed.Status);
        Assert.Contains("SSH deterministic test completed", completed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(sshRuntime.LastLaunchRequest);
        Assert.Equal("debian-root-lab", sshRuntime.LastLaunchRequest.Profile);
        Assert.Equal("deterministic SSH whoami workflow", sshRuntime.LastLaunchRequest.Objective);
        Assert.Equal(start.RunId.ToString(), sshRuntime.LastLaunchRequest.OperationKey);
        Assert.Single(sshRuntime.LastLaunchRequest.Commands);
        Assert.Equal("whoami", sshRuntime.LastLaunchRequest.Commands[0].Command);

        var traceStatuses = await ReadTraceStatusesAsync(database.ConnectionString, start.RunId, TestContext.Current.CancellationToken);
        Assert.Collection(
            traceStatuses,
            static status => Assert.Equal(AgentRunStatuses.Queued, status),
            static status => Assert.Equal(AgentRunStatuses.Planning, status),
            static status => Assert.Equal(AgentRunStatuses.AwaitingApproval, status),
            static status => Assert.Equal(AgentRunStatuses.Queued, status),
            static status => Assert.Equal(AgentRunStatuses.Planning, status),
            static status => Assert.Equal(AgentRunStatuses.Working, status),
            static status => Assert.Equal(AgentRunStatuses.Completed, status));
    }

    private static IContainer BuildContainer(string connectionString)
    {
        var builder = new ContainerBuilder();

        builder.RegisterGeneric(typeof(NullLogger<>))
            .As(typeof(ILogger<>))
            .SingleInstance();

        builder.RegisterInstance(new AgentRouterSqliteOptions
            {
                ConnectionString = connectionString,
                EnsureCreatedOnUse = true
            })
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FakeSshAgentRuntime>()
            .AsSelf()
            .As<ISshAgentRuntime>()
            .SingleInstance();

        builder.RegisterModule(new AgentRouterApplicationModule());
        builder.RegisterModule(new AgentRouterInfrastructureModule());
        builder.RegisterModule(new ExecutionPluginsSshModule());

        return builder.Build();
    }

    private static AgentObjective CreateObjective(string value)
    {
        if (AgentObjective.TryCreate(value, out var objective))
        {
            return objective;
        }

        throw new InvalidOperationException($"Invalid test objective '{value}'.");
    }

    private static async ValueTask<IReadOnlyList<string>> ReadTraceStatusesAsync(
        string connectionString,
        AgentRunId runId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status
            FROM agent_run_traces
            WHERE run_id = $runId
            ORDER BY trace_id
            """;
        command.Parameters.AddWithValue("$runId", runId.Value);

        var statuses = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            statuses.Add(reader.GetString(0));
        }

        return statuses;
    }

    private sealed class FakeSshAgentRuntime : ISshAgentRuntime
    {
        public SshAgentLaunchRequest? LastLaunchRequest { get; private set; }

        public ValueTask<Fin<SshAgentLaunchResponse>> LaunchAsync(
            SshAgentLaunchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastLaunchRequest = request;

            return new ValueTask<Fin<SshAgentLaunchResponse>>(Fin.Succ(new SshAgentLaunchResponse
            {
                AgentId = "ssh-agent-deterministic-test-id",
                Status = AgentRunStatuses.Completed,
                Profile = request.Profile,
                Objective = request.Objective,
                CommandCount = request.Commands.Count,
                CurrentStep = request.Commands.Count,
                PollIntervalMilliseconds = 250,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                Summary = "SSH deterministic test completed."
            }));
        }

        public ValueTask<Fin<SshAgentStatusResponse>> GetStatusAsync(
            string agentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Fin<SshAgentStatusResponse>>(
                Fin.Fail<SshAgentStatusResponse>(Error.New("Status polling is not used by deterministic workflow integration tests.")));
        }

        public ValueTask<Fin<SshAgentOutputResponse>> GetOutputAsync(
            SshAgentOutputRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Fin<SshAgentOutputResponse>>(
                Fin.Fail<SshAgentOutputResponse>(Error.New("Output polling is not used by deterministic workflow integration tests.")));
        }

        public ValueTask<Fin<SshAgentCancelResponse>> CancelAsync(
            string agentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Fin<SshAgentCancelResponse>>(
                Fin.Fail<SshAgentCancelResponse>(Error.New("Cancel is not used by deterministic workflow integration tests.")));
        }
    }

    private sealed class TempSqliteDatabase : IAsyncDisposable
    {
        private readonly string _path;

        private TempSqliteDatabase(string path)
        {
            _path = path;
            ConnectionString = $"Data Source={path};Cache=Shared;Pooling=False";
        }

        public string ConnectionString { get; }

        public static TempSqliteDatabase Create()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "agent-router-deterministic-ssh-" + Guid.NewGuid().ToString("N") + ".db");

            return new TempSqliteDatabase(path);
        }

        public async ValueTask DisposeAsync()
        {
            await DeleteIfExistsAsync(_path).ConfigureAwait(false);
            await DeleteIfExistsAsync(_path + "-wal").ConfigureAwait(false);
            await DeleteIfExistsAsync(_path + "-shm").ConfigureAwait(false);
        }

        private static async ValueTask DeleteIfExistsAsync(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            IOException? lastException = null;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.Delete(path);
                    return;
                }
                catch (IOException exception)
                {
                    lastException = exception;
                    await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken)
                        .ConfigureAwait(false);
                }
            }

            throw lastException ?? new IOException($"Could not delete temporary SQLite database file '{path}'.");
        }
    }
}
