using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class RootsListChangedNotificationHandler : IMcpMethodHandler
{
    private readonly IMcpSessionState _sessionState;

    public RootsListChangedNotificationHandler(IMcpSessionState sessionState)
    {
        ArgumentNullException.ThrowIfNull(sessionState);
        _sessionState = sessionState;
    }

    public string Method => McpMethods.NotificationsRootsListChanged;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessionState.MarkRootsListChanged();
        return new ValueTask<Fin<JsonElement>>(Fin.Succ<JsonElement>(McpJsonElements.EmptyObject));
    }
}
