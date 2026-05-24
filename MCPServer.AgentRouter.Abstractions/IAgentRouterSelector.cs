using LanguageExt;
using MCPServer.AgentRouter.Abstractions.Models;

namespace MCPServer.AgentRouter.Abstractions;

public interface IAgentRouterSelector
{
    ValueTask<Fin<IAgentRouter>> SelectAsync(
        in AgentRouterProviderRequest request,
        CancellationToken cancellationToken);
}
