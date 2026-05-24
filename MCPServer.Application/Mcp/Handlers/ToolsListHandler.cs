using System.Text.Json;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class ToolsListHandler : IMcpMethodHandler
{
    private readonly IMcpToolRegistry _toolRegistry;

    public ToolsListHandler(IMcpToolRegistry toolRegistry)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _toolRegistry = toolRegistry;
    }

    public string Method => McpMethods.ToolsList;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cursorRead = ReadCursor(parameters);
        if (cursorRead is not { IsSuccess: true })
        {
            return Fail(cursorRead.Error is { } cursorError ? cursorError.Message : "tools/list cursor could not be read.");
        }

        var outcome = _toolRegistry.ListTools(cursorRead.Cursor).Match(
            Succ: static result => ListOutcome.Success(result),
            Fail: static error => ListOutcome.Fail(error));

        if (outcome is not { IsSuccess: true, Result: { } result })
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(outcome.Error ?? Error.New("tools/list failed.")));
        }

        var payload = JsonSerializer.SerializeToElement(result, McpJsonSerializerContext.Default.ToolsListResult);
        return new ValueTask<Fin<JsonElement>>(Fin.Succ<JsonElement>(payload));
    }

    private static CursorReadResult ReadCursor(JsonElement? parameters)
    {
        if (parameters is not { } suppliedParameters)
        {
            return CursorReadResult.Success(default);
        }

        if (suppliedParameters is not { ValueKind: JsonValueKind.Object })
        {
            return CursorReadResult.Fail(Error.New("tools/list parameters must be an object when supplied."));
        }

        try
        {
            var request = suppliedParameters.Deserialize(McpJsonSerializerContext.Default.CursorRequestParams);
            return CursorReadResult.Success(request?.Cursor);
        }
        catch (JsonException ex)
        {
            return CursorReadResult.Fail(Error.New($"tools/list parameters are invalid JSON: {ex.Message}"));
        }
    }

    private static ValueTask<Fin<JsonElement>> Fail(string message)
    {
        return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New(message)));
    }

    private readonly struct CursorReadResult
    {
        private CursorReadResult(string? cursor, Error? error, bool isSuccess)
        {
            Cursor = cursor;
            Error = error;
            IsSuccess = isSuccess;
        }

        public string? Cursor { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static CursorReadResult Success(string? cursor)
        {
            return new CursorReadResult(cursor, default, isSuccess: true);
        }

        public static CursorReadResult Fail(Error error)
        {
            return new CursorReadResult(default, error, isSuccess: false);
        }
    }

    private readonly struct ListOutcome
    {
        private ListOutcome(ToolsListResult? result, Error? error, bool isSuccess)
        {
            Result = result;
            Error = error;
            IsSuccess = isSuccess;
        }

        public ToolsListResult? Result { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static ListOutcome Success(ToolsListResult result)
        {
            return new ListOutcome(result, default, isSuccess: true);
        }

        public static ListOutcome Fail(Error error)
        {
            return new ListOutcome(default, error, isSuccess: false);
        }
    }
}
