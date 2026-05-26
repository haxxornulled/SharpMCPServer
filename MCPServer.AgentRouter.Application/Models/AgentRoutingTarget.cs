using MCPServer.AgentRouter.Domain.Policies;

namespace MCPServer.AgentRouter.Application.Models;

public sealed record AgentRoutingTarget(
    string Name,
    AgentRoutingTargetKind Kind,
    string RiskLevel,
    bool RequiresApproval,
    string Description)
{
    public static AgentRoutingTarget LocalModel { get; } = new(
        Name: "local-model",
        Kind: AgentRoutingTargetKind.LocalModel,
        RiskLevel: AgentExecutionRiskLevels.Low,
        RequiresApproval: false,
        Description: "Host-local model routing that stays within the process boundary.");

    public static AgentRoutingTarget RemoteApi { get; } = new(
        Name: "remote-api",
        Kind: AgentRoutingTargetKind.RemoteApi,
        RiskLevel: AgentExecutionRiskLevels.Medium,
        RequiresApproval: true,
        Description: "Remote API routing that must not proceed without approval.");

    public static AgentRoutingTarget McpServer { get; } = new(
        Name: "mcp-server",
        Kind: AgentRoutingTargetKind.McpServer,
        RiskLevel: AgentExecutionRiskLevels.High,
        RequiresApproval: true,
        Description: "MCP server routing that can reach external tools and side effects.");
}
