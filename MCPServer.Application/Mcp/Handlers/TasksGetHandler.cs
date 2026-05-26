using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class TasksGetHandler : IMcpMethodHandler
{
    private readonly IMcpTaskRegistry _taskRegistry;

    public TasksGetHandler(IMcpTaskRegistry taskRegistry)
    {
        ArgumentNullException.ThrowIfNull(taskRegistry);
        _taskRegistry = taskRegistry;
    }

    public string Method => McpMethods.TasksGet;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parameters is not { } suppliedParameters)
        {
            return Fail("tasks/get parameters are required.");
        }

        GetTaskRequest? request;
        try
        {
            request = suppliedParameters.Deserialize(McpJsonSerializerContext.Default.GetTaskRequest);
        }
        catch (JsonException ex)
        {
            return Fail($"tasks/get parameters are invalid JSON: {ex.Message}");
        }

        if (request is not { TaskId: { Length: > 0 } taskId })
        {
            return Fail("tasks/get requires a non-empty taskId.");
        }

        var outcome = _taskRegistry.GetTask(taskId);
        if (outcome.IsFail)
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(outcome.Match(Succ: _ => Error.New("Unexpected success."), Fail: static e => e)));
        }

        var payload = JsonSerializer.SerializeToElement(outcome.Match(Succ: static x => x, Fail: _ => throw new InvalidOperationException()), McpJsonSerializerContext.Default.GetTaskResult);
        return new ValueTask<Fin<JsonElement>>(Fin.Succ<JsonElement>(payload));
    }

    private static ValueTask<Fin<JsonElement>> Fail(string message) => new(Fin.Fail<JsonElement>(Error.New(message)));
}
