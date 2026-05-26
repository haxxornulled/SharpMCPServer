using LanguageExt;
using MCPServer.Execution.Abstractions.Models;
using MCPServer.AgentRouter.Domain.Capabilities;

namespace MCPServer.Execution.Abstractions;

public interface IAgentPlugin
{
    string Name { get; }

    IReadOnlyList<AgentCapabilityDescriptor> Capabilities { get; }

    bool CanHandle(AgentPluginExecutionRequest request);

    ValueTask<Fin<AgentPluginExecutionResult>> ExecuteAsync(
        AgentPluginExecutionRequest request,
        CancellationToken cancellationToken);
}
