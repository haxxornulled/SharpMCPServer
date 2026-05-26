using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Workflows;

namespace MCPServer.AgentRouter.Application.Models;

public sealed record AgentModelContext(
    AgentObjective Objective,
    AgentWorkflowProfile WorkflowProfile,
    AgentRoutingTarget Target,
    IReadOnlyDictionary<string, string?> PromptMetadata,
    bool ApprovalGranted)
{
    public bool CanProceed => ApprovalGranted || !Target.RequiresApproval;
}
