using LanguageExt;
using MCPServer.Execution.Abstractions.Models;
using MCPServer.Execution.Abstractions;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentPluginRegistry
{
    IReadOnlyList<IAgentPlugin> Plugins { get; }

    ValueTask<Fin<IAgentPlugin>> SelectAsync(
        AgentPluginExecutionRequest request,
        CancellationToken cancellationToken);
}
