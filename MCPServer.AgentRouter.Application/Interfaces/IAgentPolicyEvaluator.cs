using LanguageExt;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Domain.Policies;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentPolicyEvaluator
{
    ValueTask<Fin<AgentPolicyDecision>> EvaluateAsync(
        in AgentToolExecutionRequest request,
        CancellationToken cancellationToken);
}
