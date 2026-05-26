using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class TasksListHandler : IMcpMethodHandler
{
    private readonly IMcpTaskRegistry _taskRegistry;

    public TasksListHandler(IMcpTaskRegistry taskRegistry)
    {
        ArgumentNullException.ThrowIfNull(taskRegistry);
        _taskRegistry = taskRegistry;
    }

    public string Method => McpMethods.TasksList;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cursorRead = ReadCursor(parameters);
        if (cursorRead is not { IsSuccess: true })
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(cursorRead.Error ?? Error.New("tasks/list cursor could not be read.")));
        }

        var outcome = _taskRegistry.ListTasks(cursorRead.Cursor).Match(
            Succ: static result => result,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var payload = JsonSerializer.SerializeToElement(outcome, McpJsonSerializerContext.Default.ListTasksResult);
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
            return CursorReadResult.Fail(Error.New("tasks/list parameters must be an object when supplied."));
        }

        try
        {
            var request = suppliedParameters.Deserialize(McpJsonSerializerContext.Default.CursorRequestParams);
            return CursorReadResult.Success(request?.Cursor);
        }
        catch (JsonException ex)
        {
            return CursorReadResult.Fail(Error.New($"tasks/list parameters are invalid JSON: {ex.Message}"));
        }
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
        public static CursorReadResult Success(string? cursor) => new(cursor, default, true);
        public static CursorReadResult Fail(Error error) => new(default, error, false);
    }
}
