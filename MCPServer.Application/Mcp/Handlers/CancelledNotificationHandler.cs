using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using Microsoft.Extensions.Logging;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class CancelledNotificationHandler : IMcpMethodHandler
{
    private readonly IMcpRequestExecutionRegistry _requestExecutionRegistry;
    private readonly ILogger<CancelledNotificationHandler> _logger;

    public CancelledNotificationHandler(
        IMcpRequestExecutionRegistry requestExecutionRegistry,
        ILogger<CancelledNotificationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(requestExecutionRegistry);
        ArgumentNullException.ThrowIfNull(logger);
        _requestExecutionRegistry = requestExecutionRegistry;
        _logger = logger;
    }

    public string Method => McpMethods.NotificationsCancelled;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (parameters is not { } suppliedParameters)
        {
            return SucceedIgnored();
        }

        CancelledNotificationParams? notification;
        try
        {
            notification = suppliedParameters.Deserialize(McpJsonSerializerContext.Default.CancelledNotificationParams);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Ignoring malformed MCP cancellation notification");
            return SucceedIgnored();
        }

        if (notification is not { } cancelled || !JsonRpcRequestId.TryFromElement(cancelled.RequestId, out var requestId))
        {
            return SucceedIgnored();
        }

        _requestExecutionRegistry.TryCancel(requestId, cancelled.Reason);
        return SucceedIgnored();
    }

    private static ValueTask<Fin<JsonElement>> SucceedIgnored()
    {
        return new ValueTask<Fin<JsonElement>>(Fin.Succ<JsonElement>(McpJsonElements.EmptyObject));
    }
}
