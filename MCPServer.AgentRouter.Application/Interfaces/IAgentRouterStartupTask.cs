using LanguageExt;

namespace MCPServer.AgentRouter.Application.Interfaces;

public interface IAgentRouterStartupTask
{
    string Name { get; }

    ValueTask<Fin<Unit>> ExecuteAsync(CancellationToken cancellationToken);
}
