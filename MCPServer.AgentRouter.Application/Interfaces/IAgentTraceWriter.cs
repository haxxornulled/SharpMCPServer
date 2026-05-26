using LanguageExt;
using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentTraceWriter
{
    ValueTask<Fin<Unit>> WriteSnapshotAsync(
        AgentRunSnapshot snapshot,
        CancellationToken cancellationToken);
}
