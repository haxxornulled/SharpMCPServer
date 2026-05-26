using MCPServer.AgentRouter.Domain.Plans;
using MCPServer.AgentRouter.Domain.Policies;

namespace MCPServer.AgentRouter.Application.Models;

public sealed record AgentRoutingDecision(
    AgentModelContext Context,
    AgentPlan Plan,
    AgentRoutingTarget Target,
    AgentPolicyDecision PolicyDecision,
    string Message)
{
    public bool CanProceed => PolicyDecision.IsAllowed;

    public bool RequiresApproval => PolicyDecision.RequiresApproval;
}
