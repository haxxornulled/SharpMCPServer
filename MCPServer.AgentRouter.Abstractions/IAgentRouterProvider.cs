using LanguageExt;
using MCPServer.AgentRouter.Abstractions.Models;

namespace MCPServer.AgentRouter.Abstractions;

public interface IAgentRouterProvider
{
    ValueTask<Fin<IAgentRouter>> GetRouterAsync(
        in AgentRouterProviderRequest request,
        CancellationToken cancellationToken);
}
