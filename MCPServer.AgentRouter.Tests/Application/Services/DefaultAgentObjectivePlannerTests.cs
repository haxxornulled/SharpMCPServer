using MCPServer.AgentRouter.Application.Services;
using MCPServer.AgentRouter.Application.Tests.Testing;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Plans;
using Xunit;

namespace MCPServer.AgentRouter.Application.Tests.Services;

public sealed class DefaultAgentObjectivePlannerTests
{
    [Fact]
    public async Task PlanAsync_Produces_Stable_Planning_Steps()
    {
        var planner = new DefaultAgentObjectivePlanner();
        Assert.True(AgentObjective.TryCreate("review the host-side routing boundary", out var objective));

        var result = await planner.PlanAsync(objective, TestContext.Current.CancellationToken);
        var plan = TestFin.Success(result);

        Assert.Equal(objective, plan.Objective);
        Assert.Equal(4, plan.Steps.Count);
        Assert.Collection(
            plan.Steps,
            step =>
            {
                Assert.Equal(1, step.Order);
                Assert.Equal("Normalize objective", step.Name);
                Assert.Equal("planning.normalize", step.CapabilityName);
            },
            step =>
            {
                Assert.Equal(2, step.Order);
                Assert.Equal("Build model context", step.Name);
                Assert.Equal("planning.context", step.CapabilityName);
            },
            step =>
            {
                Assert.Equal(3, step.Order);
                Assert.Equal("Apply approval boundary", step.Name);
                Assert.Equal("planning.approval", step.CapabilityName);
            },
            step =>
            {
                Assert.Equal(4, step.Order);
                Assert.Equal("Select execution route", step.Name);
                Assert.Equal("planning.routing", step.CapabilityName);
            });
        Assert.Contains("host-side plan", plan.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanAsync_Rejects_Empty_Objective()
    {
        var planner = new DefaultAgentObjectivePlanner();

        var failure = TestFin.Failure(await planner.PlanAsync(default, TestContext.Current.CancellationToken));

        Assert.Contains("objective", failure.Message, StringComparison.OrdinalIgnoreCase);
    }
}
