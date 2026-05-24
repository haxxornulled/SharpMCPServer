using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class InitializedNotificationHandler : IMcpMethodHandler
{
    private readonly IMcpSessionState _sessionState;

    public InitializedNotificationHandler(IMcpSessionState sessionState)
    {
        ArgumentNullException.ThrowIfNull(sessionState);
        _sessionState = sessionState;
    }

    public string Method => McpMethods.NotificationsInitialized;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessionState.MarkInitializedNotificationReceived();
        return new ValueTask<Fin<JsonElement>>(Fin.Succ<JsonElement>(McpJsonElements.EmptyObject));
    }
}
