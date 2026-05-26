using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class TasksResultHandler : IMcpMethodHandler
{
    private readonly IMcpTaskRegistry _taskRegistry;

    public TasksResultHandler(IMcpTaskRegistry taskRegistry)
    {
        ArgumentNullException.ThrowIfNull(taskRegistry);
        _taskRegistry = taskRegistry;
    }

    public string Method => McpMethods.TasksResult;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parameters is not { } suppliedParameters)
        {
            return Fail("tasks/result parameters are required.");
        }

        GetTaskPayloadRequest? request;
        try
        {
            request = suppliedParameters.Deserialize(McpJsonSerializerContext.Default.GetTaskPayloadRequest);
        }
        catch (JsonException ex)
        {
            return Fail($"tasks/result parameters are invalid JSON: {ex.Message}");
        }

        if (request is not { TaskId: { Length: > 0 } taskId })
        {
            return Fail("tasks/result requires a non-empty taskId.");
        }

        return new ValueTask<Fin<JsonElement>>(_taskRegistry.GetTaskResultPayload(taskId));
    }

    private static ValueTask<Fin<JsonElement>> Fail(string message) => new(Fin.Fail<JsonElement>(Error.New(message)));
}
