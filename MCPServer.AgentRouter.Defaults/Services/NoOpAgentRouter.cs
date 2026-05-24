using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;

namespace MCPServer.AgentRouter.Defaults.Services;

public sealed class NoOpAgentRouter : IAgentRouter
{
    public ValueTask<Fin<AgentRouterRunResult>> RunAsync(
        in AgentRouterRunRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Objective))
        {
            return new ValueTask<Fin<AgentRouterRunResult>>(
                Fin.Fail<AgentRouterRunResult>(Error.New("agent objective is required.")));
        }

        var timestamp = DateTimeOffset.UtcNow;
        var result = new AgentRouterRunResult(
            Status: AgentRouterRunStatuses.Disabled,
            Message: "The default AgentRouter package is registered, but objective execution is not enabled in this slice.",
            RunId: null,
            StartedAtUtc: timestamp,
            CompletedAtUtc: timestamp);

        return new ValueTask<Fin<AgentRouterRunResult>>(Fin.Succ(result));
    }
}
