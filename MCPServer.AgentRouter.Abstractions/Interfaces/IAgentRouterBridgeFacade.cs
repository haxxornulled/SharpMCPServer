using LanguageExt;
using MCPServer.AgentRouter.Abstractions.Models;

namespace MCPServer.AgentRouter.Abstractions.Interfaces;

public interface IAgentRouterBridgeFacade
{
    ValueTask<Fin<AgentRouterBridgeResponse>> RunAsync(
        in AgentRouterBridgeRequest request,
        CancellationToken cancellationToken);
}
