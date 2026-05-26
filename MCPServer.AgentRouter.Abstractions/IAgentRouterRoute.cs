using LanguageExt;
using MCPServer.AgentRouter.Abstractions.Models;

namespace MCPServer.AgentRouter.Abstractions;

public interface IAgentRouterRoute
{
    string Name { get; }

    int Order { get; }

    bool IsMatch(in AgentRouterProviderRequest request);

    ValueTask<Fin<IAgentRouter>> GetRouterAsync(
        in AgentRouterProviderRequest request,
        CancellationToken cancellationToken);
}
