using LanguageExt;
using MCPServer.AgentRouter.Abstractions.Models;

namespace MCPServer.AgentRouter.Abstractions;

public interface IAgentRouter
{
    ValueTask<Fin<AgentRouterRunResult>> RunAsync(
        in AgentRouterRunRequest request,
        CancellationToken cancellationToken);
}
