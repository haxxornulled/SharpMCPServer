using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Application.Services;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Domain.Workflows;
using MCPServer.Application.Mcp.Tools;
using Xunit;

namespace MCPServer.UnitTests.Application.Mcp.Tools;

public sealed class AgentToolHelpersTests
{
    [Fact]
    public void BuildStructuredContent_Strips_ApprovalToken_From_Public_Metadata()
    {
        var snapshot = new AgentRunSnapshot(
            new AgentRunId("run-123"),
            new AgentObjective("Improve the workspace audit"),
            "awaiting_approval",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow,
            "Waiting for approval.",
            Version: 3,
            Metadata: new Dictionary<string, string?>
            {
                [AgentRouterMetadataKeys.Capability] = "planning.approval",
                [AgentRouterMetadataKeys.Kind] = "agent",
                [AgentRouterMetadataKeys.ApprovalToken] = "super-secret-token",
                [AgentRouterMetadataKeys.ApprovalGranted] = "true",
                ["custom.note"] = "kept"
            });

        var structuredContent = AgentToolHelpers.BuildStructuredContent(snapshot);

        Assert.True(structuredContent.ApprovalGranted);
        Assert.True(structuredContent.Metadata.ContainsKey("custom.note"));
        Assert.DoesNotContain(AgentRouterMetadataKeys.ApprovalToken, structuredContent.Metadata.Keys);
    }
}
