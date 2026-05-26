using Autofac;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.Execution.Abstractions.Models;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.ExecutionPlugins.Ssh.Services;
using MCPServer.ExecutionPlugins.Ssh.Tests.Testing;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Models;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPServer.Execution.Abstractions;

namespace MCPServer.ExecutionPlugins.Ssh.Tests.Services;

public sealed class SshAgentPluginTests
{
    [Fact]
    public void ExecutionPluginsSshModule_Registers_Generic_Agent_Plugin()
    {
        using var container = BuildContainer();

        var plugin = container.Resolve<IAgentPlugin>();

        Assert.IsType<SshAgentPlugin>(plugin);
        Assert.Equal(SshAgentPlugin.PluginName, plugin.Name);
        Assert.Contains(plugin.Capabilities, static capability => capability.Name.Value == AgentRouterSshCapabilityNames.RemoteShell);
    }

    [Fact]
    public async Task SshAgentPlugin_Adapts_Generic_Request_To_Existing_SshAgentRuntime()
    {
        using var container = BuildContainer();
        var plugin = container.Resolve<IAgentPlugin>();
        var runtime = container.Resolve<FakeSshAgentRuntime>();
        Assert.True(AgentObjective.TryCreate("validate remote user", out var objective));
        var runId = AgentRunId.New();
        var request = new AgentPluginExecutionRequest(
            runId,
            objective,
            AgentRouterSshCapabilityNames.RemoteShell,
            new Dictionary<string, string?>
            {
                [AgentRouterSshMetadataKeys.Profile] = "debian-root-lab",
                [AgentRouterSshMetadataKeys.Command] = "whoami",
                [AgentRouterSshMetadataKeys.ArgumentsJson] = "[]"
            });

        var result = TestFin.Success(await plugin.ExecuteAsync(request, TestContext.Current.CancellationToken));

        Assert.True(result.Succeeded);
        Assert.Equal("ssh-agent-test-id", result.ExternalRunId);
        Assert.NotNull(runtime.LastRequest);
        Assert.Equal("debian-root-lab", runtime.LastRequest.Profile);
        Assert.Equal("validate remote user", runtime.LastRequest.Objective);
        Assert.Equal(runId.ToString(), runtime.LastRequest.OperationKey);
        Assert.Single(runtime.LastRequest.Commands);
        Assert.Equal("whoami", runtime.LastRequest.Commands[0].Command);
    }

    [Fact]
    public async Task SshAgentPlugin_Rejects_Missing_Profile_Before_Calling_Runtime()
    {
        using var container = BuildContainer();
        var plugin = container.Resolve<IAgentPlugin>();
        var runtime = container.Resolve<FakeSshAgentRuntime>();
        Assert.True(AgentObjective.TryCreate("validate remote user", out var objective));
        var request = new AgentPluginExecutionRequest(
            AgentRunId.New(),
            objective,
            AgentRouterSshCapabilityNames.RemoteShell,
            new Dictionary<string, string?>
            {
                [AgentRouterSshMetadataKeys.Command] = "whoami"
            });

        var failure = TestFin.Failure(await plugin.ExecuteAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("profile", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(runtime.LastRequest);
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();

        builder.RegisterGeneric(typeof(NullLogger<>))
            .As(typeof(ILogger<>))
            .SingleInstance();
        builder.RegisterType<FakeSshAgentRuntime>()
            .AsSelf()
            .As<ISshAgentRuntime>()
            .SingleInstance();
        builder.RegisterModule(new ExecutionPluginsSshModule());
        return builder.Build();
    }

    private sealed class FakeSshAgentRuntime : ISshAgentRuntime
    {
        public SshAgentLaunchRequest? LastRequest { get; private set; }

        public ValueTask<Fin<SshAgentLaunchResponse>> LaunchAsync(SshAgentLaunchRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return new ValueTask<Fin<SshAgentLaunchResponse>>(Fin.Succ(new SshAgentLaunchResponse
            {
                AgentId = "ssh-agent-test-id",
                Status = "queued",
                Profile = request.Profile,
                Objective = request.Objective,
                CommandCount = request.Commands.Count,
                CurrentStep = 0,
                PollIntervalMilliseconds = 1000,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                Summary = "SSH agent queued."
            }));
        }

        public ValueTask<Fin<SshAgentStatusResponse>> GetStatusAsync(string agentId, CancellationToken cancellationToken)
        {
            return new ValueTask<Fin<SshAgentStatusResponse>>(Fin.Fail<SshAgentStatusResponse>(Error.New("Not used by this test.")));
        }

        public ValueTask<Fin<SshAgentOutputResponse>> GetOutputAsync(SshAgentOutputRequest request, CancellationToken cancellationToken)
        {
            return new ValueTask<Fin<SshAgentOutputResponse>>(Fin.Fail<SshAgentOutputResponse>(Error.New("Not used by this test.")));
        }

        public ValueTask<Fin<SshAgentCancelResponse>> CancelAsync(string agentId, CancellationToken cancellationToken)
        {
            return new ValueTask<Fin<SshAgentCancelResponse>>(Fin.Fail<SshAgentCancelResponse>(Error.New("Not used by this test.")));
        }
    }
}
