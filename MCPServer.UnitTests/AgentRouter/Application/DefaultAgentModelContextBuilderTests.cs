using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Application.Services;
using Xunit;

namespace MCPServer.UnitTests.AgentRouter.Application;

public sealed class DefaultAgentModelContextBuilderTests
{
    [Fact]
    public void Build_Strips_ApprovalToken_From_Prompt_Metadata()
    {
        var builder = new DefaultAgentModelContextBuilder();
        var request = new AgentRouterRunRequest(
            "Improve the workspace audit",
            new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.Capability] = "planning.approval",
                [AgentRouterMetadataKeys.WorkflowMode] = "agentic",
                [AgentRouterMetadataKeys.RouteTarget] = "remote",
                [AgentRouterMetadataKeys.ApprovalToken] = "super-secret-token",
                [AgentRouterMetadataKeys.ApprovalGranted] = "true",
                ["custom.note"] = "kept"
            });

        var result = builder.Build(in request);

        Assert.True(result.IsSucc);
        var context = result.Match(Succ: static value => value, Fail: static _ => throw new InvalidOperationException());
        Assert.True(context.ApprovalGranted);
        Assert.True(context.PromptMetadata.ContainsKey("custom.note"));
        Assert.DoesNotContain(AgentRouterMetadataKeys.ApprovalToken, context.PromptMetadata.Keys);
        Assert.DoesNotContain(AgentRouterMetadataKeys.Capability, context.PromptMetadata.Keys);
    }
}
