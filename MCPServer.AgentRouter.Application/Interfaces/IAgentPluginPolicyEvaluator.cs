using LanguageExt;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Domain.Policies;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentPluginPolicyEvaluator
{
    ValueTask<Fin<AgentPolicyDecision>> EvaluateAsync(
        AgentPluginPolicyRequest request,
        CancellationToken cancellationToken);
}
