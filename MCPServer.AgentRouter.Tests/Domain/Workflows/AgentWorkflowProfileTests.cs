using MCPServer.AgentRouter.Domain.Workflows;
using Xunit;

namespace MCPServer.AgentRouter.Domain.Tests.Workflows;

public sealed class AgentWorkflowProfileTests
{
    [Fact]
    public void Deterministic_Profile_Represents_Predetermined_Validate_Approve_Execute_Path()
    {
        var profile = AgentWorkflowProfile.Deterministic();

        Assert.True(profile.IsDeterministic);
        Assert.False(profile.IsAgentic);
        Assert.True(profile.RequiresApprovalBeforeExecution);
        Assert.False(profile.AllowsDynamicPlanning);
        Assert.False(profile.AllowsInspectAndContinue);
        Assert.Contains("predetermined", profile.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Agentic_Profile_Allows_Planning_Inspection_And_Continuation()
    {
        var profile = AgentWorkflowProfile.Agentic();

        Assert.True(profile.IsAgentic);
        Assert.False(profile.IsDeterministic);
        Assert.True(profile.RequiresApprovalBeforeExecution);
        Assert.True(profile.AllowsDynamicPlanning);
        Assert.True(profile.AllowsInspectAndContinue);
        Assert.Contains("dynamically", profile.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("deterministic", true, false)]
    [InlineData("DETERMINISTIC", true, false)]
    [InlineData("agentic", false, true)]
    [InlineData(" AGENTIC ", false, true)]
    public void Workflow_Mode_Normalizes_Known_Values(string value, bool expectedDeterministic, bool expectedAgentic)
    {
        var created = AgentWorkflowMode.TryCreate(value, out var mode);

        Assert.True(created);
        Assert.Equal(expectedDeterministic, mode.IsDeterministic);
        Assert.Equal(expectedAgentic, mode.IsAgentic);
    }

    [Fact]
    public void Workflow_Mode_Rejects_Unknown_Value()
    {
        var created = AgentWorkflowMode.TryCreate("random", out var mode);

        Assert.False(created);
        Assert.True(mode.IsEmpty);
    }

    [Fact]
    public void TryCreate_Profile_Maps_Mode_To_Profile()
    {
        var created = AgentWorkflowProfile.TryCreate(AgentWorkflowMode.Agentic, out var profile);

        Assert.True(created);
        Assert.True(profile.IsAgentic);
    }
}
