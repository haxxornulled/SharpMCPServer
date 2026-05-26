using LanguageExt;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;

namespace MCPServer.AgentRouter.Hosting.Services;

public sealed class DefaultAgentRouterProvider : IAgentRouterProvider
{
    private readonly IAgentRouterSelector _selector;

    public DefaultAgentRouterProvider(IAgentRouterSelector selector)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
    }

    public ValueTask<Fin<IAgentRouter>> GetRouterAsync(
        in AgentRouterProviderRequest request,
        CancellationToken cancellationToken)
    {
        return _selector.SelectAsync(in request, cancellationToken);
    }
}
