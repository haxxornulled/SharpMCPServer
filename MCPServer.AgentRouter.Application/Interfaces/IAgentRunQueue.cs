using LanguageExt;
using MCPServer.AgentRouter.Application.WorkItems;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentRunQueue
{
    ValueTask<Fin<Unit>> EnqueueAsync(
        AgentRunWorkItem workItem,
        CancellationToken cancellationToken);

    ValueTask<Fin<AgentRunWorkItem>> DequeueAsync(
        CancellationToken cancellationToken);
}
