using LanguageExt;
using MCPServer.Execution.Abstractions.Models;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentToolExecutor
{
    ValueTask<Fin<AgentToolExecutionResult>> ExecuteAsync(
        in AgentToolExecutionRequest request,
        CancellationToken cancellationToken);
}
