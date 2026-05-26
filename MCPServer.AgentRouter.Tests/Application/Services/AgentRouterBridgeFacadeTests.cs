using Autofac;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Interfaces;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPServer.AgentRouter.Tests.Application.Services;

public sealed class AgentRouterBridgeFacadeTests
{
    [Fact]
    public void BridgeFacade_Is_Registered_As_Singleton()
    {
        using var container = BuildContainer();

        var first = container.Resolve<IAgentRouterBridgeFacade>();
        var second = container.Resolve<IAgentRouterBridgeFacade>();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task RunAsync_Returns_Completed_For_Local_Objective()
    {
        using var container = BuildContainer();
        var facade = container.Resolve<IAgentRouterBridgeFacade>();

        var result = await facade.RunAsync(
            new AgentRouterBridgeRequest(
                Objective: "Summarize the current workspace",
                Metadata: null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSucc);

        var response = result.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        Assert.Equal(AgentRouterRunStatuses.Completed, response.Status);
        Assert.Contains("local-model", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(response.RunId);
        Assert.True(response.CompletedAtUtc >= response.StartedAtUtc);
    }

    [Fact]
    public async Task RunAsync_Denies_Remote_Target_Without_Approval()
    {
        using var container = BuildContainer();
        var facade = container.Resolve<IAgentRouterBridgeFacade>();

        var result = await facade.RunAsync(
            new AgentRouterBridgeRequest(
                Objective: "Reach out to the remote model",
                Metadata: new Dictionary<string, string?>
                {
                    [AgentRouterMetadataKeys.RouteTarget] = "remote-api"
                }),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSucc);

        var response = result.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        Assert.Equal(AgentRouterRunStatuses.Denied, response.Status);
        Assert.Contains("Approval token required", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(response.RunId);
        Assert.True(response.CompletedAtUtc >= response.StartedAtUtc);
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<AgentRouterApplicationModule>();
        builder.RegisterInstance(NullLoggerFactory.Instance)
            .As<ILoggerFactory>()
            .SingleInstance();
        builder.RegisterGeneric(typeof(Logger<>))
            .As(typeof(ILogger<>))
            .SingleInstance();

        return builder.Build();
    }
}
