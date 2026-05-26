using Autofac;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Application;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Application.Tests.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPServer.AgentRouter.Application.Tests.Services;

public sealed class DefaultAgentRoutingPlannerTests
{
    [Fact]
    public async Task PlanAsync_Defaults_To_Local_Model_And_Strips_Control_Metadata()
    {
        using var container = BuildContainer();
        var planner = container.Resolve<IAgentRoutingPlanner>();
        var request = new AgentRouterRunRequest(
            Objective: "review the host-side routing boundary",
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.WorkflowMode] = "deterministic",
                [AgentRouterMetadataKeys.RouteTarget] = "local-model",
                [AgentRouterMetadataKeys.ApprovalToken] = "secret-token",
                [AgentRouterMetadataKeys.ApprovalId] = "approval-123",
                ["trace.id"] = "trace-1"
            });

        var decision = TestFin.Success(await planner.PlanAsync(in request, TestContext.Current.CancellationToken));

        Assert.Equal(AgentRoutingTargetKind.LocalModel, decision.Target.Kind);
        Assert.True(decision.PolicyDecision.IsAllowed);
        Assert.False(decision.RequiresApproval);
        Assert.True(decision.Context.CanProceed);
        Assert.True(decision.Context.ApprovalGranted);
        Assert.Equal("trace-1", decision.Context.PromptMetadata["trace.id"]);
        Assert.DoesNotContain(AgentRouterMetadataKeys.RouteTarget, decision.Context.PromptMetadata.Keys);
        Assert.DoesNotContain(AgentRouterMetadataKeys.ApprovalToken, decision.Context.PromptMetadata.Keys);
        Assert.DoesNotContain(AgentRouterMetadataKeys.ApprovalId, decision.Context.PromptMetadata.Keys);
        Assert.Contains("local-model", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanAsync_Requires_Approval_For_Remote_Api_Without_Token()
    {
        using var container = BuildContainer();
        var planner = container.Resolve<IAgentRoutingPlanner>();
        var request = new AgentRouterRunRequest(
            Objective: "call the remote model and summarize the results",
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.WorkflowMode] = "agentic",
                [AgentRouterMetadataKeys.RouteTarget] = "remote-api"
            });

        var decision = TestFin.Success(await planner.PlanAsync(in request, TestContext.Current.CancellationToken));

        Assert.Equal(AgentRoutingTargetKind.RemoteApi, decision.Target.Kind);
        Assert.True(decision.PolicyDecision.RequiresApproval);
        Assert.False(decision.PolicyDecision.IsAllowed);
        Assert.Contains("approval token", decision.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AgentRouterMetadataKeys.RouteTarget, decision.Context.PromptMetadata.Keys);
    }

    [Fact]
    public async Task PlanAsync_Does_Not_Treat_Approval_Id_As_Approval()
    {
        using var container = BuildContainer();
        var planner = container.Resolve<IAgentRoutingPlanner>();
        var request = new AgentRouterRunRequest(
            Objective: "call the remote model and summarize the results",
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.WorkflowMode] = "agentic",
                [AgentRouterMetadataKeys.RouteTarget] = "remote-api",
                [AgentRouterMetadataKeys.ApprovalId] = "approval-only-123"
            });

        var decision = TestFin.Success(await planner.PlanAsync(in request, TestContext.Current.CancellationToken));

        Assert.Equal(AgentRoutingTargetKind.RemoteApi, decision.Target.Kind);
        Assert.True(decision.PolicyDecision.RequiresApproval);
        Assert.False(decision.Context.ApprovalGranted);
        Assert.False(decision.PolicyDecision.IsAllowed);
        Assert.DoesNotContain(AgentRouterMetadataKeys.ApprovalId, decision.Context.PromptMetadata.Keys);
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new AgentRouterApplicationModule());
        builder.RegisterInstance(NullLoggerFactory.Instance)
            .As<ILoggerFactory>()
            .SingleInstance();
        builder.RegisterGeneric(typeof(Logger<>))
            .As(typeof(ILogger<>))
            .SingleInstance();
        return builder.Build();
    }
}
