using LanguageExt;
using MCPServer.AgentRouter.Application.Models;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentToolExecutor
{
    ValueTask<Fin<AgentToolExecutionResult>> ExecuteAsync(
        in AgentToolExecutionRequest request,
        CancellationToken cancellationToken);
}
