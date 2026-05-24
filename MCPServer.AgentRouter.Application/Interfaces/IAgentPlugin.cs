using LanguageExt;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Domain.Capabilities;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentPlugin
{
    string Name { get; }

    IReadOnlyList<AgentCapabilityDescriptor> Capabilities { get; }

    bool CanHandle(AgentPluginExecutionRequest request);

    ValueTask<Fin<AgentPluginExecutionResult>> ExecuteAsync(
        AgentPluginExecutionRequest request,
        CancellationToken cancellationToken);
}
