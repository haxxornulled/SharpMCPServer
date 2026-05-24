using LanguageExt;
using MCPServer.AgentRouter.Application.Models;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentPluginRegistry
{
    IReadOnlyList<IAgentPlugin> Plugins { get; }

    ValueTask<Fin<IAgentPlugin>> SelectAsync(
        AgentPluginExecutionRequest request,
        CancellationToken cancellationToken);
}
