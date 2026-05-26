using LanguageExt;
using MCPServer.Execution.Abstractions.Models;
using MCPServer.AgentRouter.Domain.Policies;
using MCPServer.Execution.Abstractions;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentPluginPolicyEvaluator
{
    ValueTask<Fin<AgentPolicyDecision>> EvaluateAsync(
        AgentPluginPolicyRequest request,
        CancellationToken cancellationToken);
}
