using System.Text.Json;
using MCPServer.Domain.Mcp;

namespace MCPServer.Client.Models;

public sealed class McpClientElicitationResponse
{
    private McpClientElicitationResponse(ElicitResult? result, CreateTaskResult? task)
    {
        Result = result;
        Task = task;
    }

    public ElicitResult? Result { get; }

    public CreateTaskResult? Task { get; }

    public static McpClientElicitationResponse FromResult(ElicitResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new McpClientElicitationResponse(result, null);
    }

    public static McpClientElicitationResponse FromTask(CreateTaskResult task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new McpClientElicitationResponse(null, task);
    }

    public JsonElement ToJsonElement()
    {
        return (Result, Task) switch
        {
            ({ } result, null) => JsonSerializer.SerializeToElement(result, McpJsonSerializerContext.Default.ElicitResult),
            (null, { } task) => JsonSerializer.SerializeToElement(task, McpJsonSerializerContext.Default.CreateTaskResult),
            _ => throw new InvalidOperationException("Elicitation response must contain either a result or a task.")
        };
    }
}
