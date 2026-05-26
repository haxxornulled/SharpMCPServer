using System.Text.Json;
using MCPServer.Domain.Mcp;

namespace MCPServer.Client.Models;

public sealed class McpClientSamplingResponse
{
    private McpClientSamplingResponse(CreateMessageResult? result, CreateTaskResult? task)
    {
        Result = result;
        Task = task;
    }

    public CreateMessageResult? Result { get; }

    public CreateTaskResult? Task { get; }

    public static McpClientSamplingResponse FromResult(CreateMessageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new McpClientSamplingResponse(result, null);
    }

    public static McpClientSamplingResponse FromTask(CreateTaskResult task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new McpClientSamplingResponse(null, task);
    }

    public JsonElement ToJsonElement()
    {
        return (Result, Task) switch
        {
            ({ } result, null) => JsonSerializer.SerializeToElement(result, McpJsonSerializerContext.Default.CreateMessageResult),
            (null, { } task) => JsonSerializer.SerializeToElement(task, McpJsonSerializerContext.Default.CreateTaskResult),
            _ => throw new InvalidOperationException("Sampling response must contain either a result or a task.")
        };
    }
}
