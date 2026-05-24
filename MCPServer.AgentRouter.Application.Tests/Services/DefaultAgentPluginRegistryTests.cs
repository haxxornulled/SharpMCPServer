using Autofac;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Application.Tests.Testing;
using MCPServer.AgentRouter.Domain.Capabilities;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Policies;
using MCPServer.AgentRouter.Domain.Runs;
using Xunit;

namespace MCPServer.AgentRouter.Application.Tests.Services;

public sealed class DefaultAgentPluginRegistryTests
{
    [Fact]
    public void AgentRouterApplicationModule_Resolves_Plugin_Registry_When_No_Plugins_Are_Registered()
    {
        using var container = BuildContainer(registerPlugin: false);

        var registry = container.Resolve<IAgentPluginRegistry>();

        Assert.NotNull(registry);
        Assert.Empty(registry.Plugins);
    }

    [Fact]
    public async Task SelectAsync_Returns_Plugin_That_Can_Handle_Capability()
    {
        using var container = BuildContainer(registerPlugin: true);
        var registry = container.Resolve<IAgentPluginRegistry>();
        Assert.True(AgentObjective.TryCreate("run test capability", out var objective));
        var request = new AgentPluginExecutionRequest(
            AgentRunId.New(),
            objective,
            TestAgentPlugin.TestCapabilityName,
            new Dictionary<string, string?>(capacity: 0));

        var plugin = TestFin.Success(await registry.SelectAsync(request, TestContext.Current.CancellationToken));

        Assert.IsType<TestAgentPlugin>(plugin);
    }

    [Fact]
    public async Task SelectAsync_Rejects_Unknown_Capability()
    {
        using var container = BuildContainer(registerPlugin: true);
        var registry = container.Resolve<IAgentPluginRegistry>();
        Assert.True(AgentObjective.TryCreate("run unknown capability", out var objective));
        var request = new AgentPluginExecutionRequest(
            AgentRunId.New(),
            objective,
            "unknown-capability",
            new Dictionary<string, string?>(capacity: 0));

        var failure = TestFin.Failure(await registry.SelectAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("No AgentRouter plugin", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IContainer BuildContainer(bool registerPlugin)
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new AgentRouterApplicationModule());
        if (registerPlugin)
        {
            builder.RegisterType<TestAgentPlugin>()
                .As<IAgentPlugin>()
                .SingleInstance();
        }

        return builder.Build();
    }

    private sealed class TestAgentPlugin : IAgentPlugin
    {
        public const string TestCapabilityName = "test-capability";

        public string Name => "test";

        public IReadOnlyList<AgentCapabilityDescriptor> Capabilities { get; } =
        [
            AgentCapabilityDescriptor.Create(
                TestCapabilityName,
                "Test capability",
                AgentExecutionRiskLevels.Low,
                requiresApproval: false)
        ];

        public bool CanHandle(AgentPluginExecutionRequest request)
        {
            return string.Equals(request.CapabilityName, TestCapabilityName, StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<Fin<AgentPluginExecutionResult>> ExecuteAsync(
            AgentPluginExecutionRequest request,
            CancellationToken cancellationToken)
        {
            return new ValueTask<Fin<AgentPluginExecutionResult>>(
                Fin.Succ(AgentPluginExecutionResult.Success("completed", "Test plugin completed.", null)));
        }
    }
}
