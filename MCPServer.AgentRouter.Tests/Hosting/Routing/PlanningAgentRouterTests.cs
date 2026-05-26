using Autofac;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Hosting;
using MCPServer.AgentRouter.Hosting.Services;
using MCPServer.AgentRouter.Hosting.Tests.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPServer.AgentRouter.Hosting.Tests.Routing;

public sealed class PlanningAgentRouterTests
{
    [Fact]
    public async Task HostingModule_Registers_Planning_Route_And_Provider_Selects_It()
    {
        using var container = BuildContainer();
        var provider = container.Resolve<IAgentRouterProvider>();
        var request = new AgentRouterProviderRequest(
            RouterName: AgentRouterNames.Planning,
            Metadata: AgentRouterMetadata.Empty);

        var router = TestFin.Success(await provider.GetRouterAsync(in request, TestContext.Current.CancellationToken));

        Assert.IsType<PlanningAgentRouter>(router);
    }

    [Fact]
    public async Task PlanningRouter_Denies_Remote_Route_Without_Approval_Token()
    {
        using var container = BuildContainer();
        var router = container.Resolve<PlanningAgentRouter>();
        var request = new AgentRouterRunRequest(
            Objective: "call the remote model and summarize the results",
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.WorkflowMode] = "agentic",
                [AgentRouterMetadataKeys.RouteTarget] = "remote-api"
            });

        var result = TestFin.Success(await router.RunAsync(in request, TestContext.Current.CancellationToken));

        Assert.Equal(AgentRouterRunStatuses.Denied, result.Status);
        Assert.Contains("approval token", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanningRouter_Completes_Local_Route_Without_Approval_Token()
    {
        using var container = BuildContainer();
        var router = container.Resolve<PlanningAgentRouter>();
        var request = new AgentRouterRunRequest(
            Objective: "review the host-side routing boundary",
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.WorkflowMode] = "deterministic",
                ["trace.id"] = "trace-1"
            });

        var result = TestFin.Success(await router.RunAsync(in request, TestContext.Current.CancellationToken));

        Assert.Equal(AgentRouterRunStatuses.Completed, result.Status);
        Assert.Contains("local-model", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.RunId);
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new AgentRouterHostedProviderModule());
        builder.RegisterInstance(NullLoggerFactory.Instance)
            .As<ILoggerFactory>()
            .SingleInstance();
        builder.RegisterGeneric(typeof(Logger<>))
            .As(typeof(ILogger<>))
            .SingleInstance();
        return builder.Build();
    }
}
