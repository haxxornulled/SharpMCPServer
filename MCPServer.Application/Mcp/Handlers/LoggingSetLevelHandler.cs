using System.Text.Json;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class LoggingSetLevelHandler : IMcpMethodHandler
{
    private readonly IMcpLoggingState _loggingState;

    public LoggingSetLevelHandler(IMcpLoggingState loggingState)
    {
        ArgumentNullException.ThrowIfNull(loggingState);
        _loggingState = loggingState;
    }

    public string Method => McpMethods.LoggingSetLevel;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (parameters is not { } suppliedParameters || suppliedParameters is not { ValueKind: JsonValueKind.Object })
        {
            return Fail("logging/setLevel parameters are required and must be a JSON object.");
        }

        LoggingSetLevelRequest? request;
        try
        {
            request = suppliedParameters.Deserialize(McpJsonSerializerContext.Default.LoggingSetLevelRequest);
        }
        catch (JsonException ex)
        {
            return Fail($"logging/setLevel parameters are invalid JSON: {ex.Message}");
        }

        if (request is not { Level: { } level } || !_loggingState.TrySetMinimumLevel(level))
        {
            return Fail("logging/setLevel requires a valid MCP log level.");
        }

        return new ValueTask<Fin<JsonElement>>(Fin.Succ<JsonElement>(McpJsonElements.EmptyObject));
    }

    private static ValueTask<Fin<JsonElement>> Fail(string message)
    {
        return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New(message)));
    }
}
