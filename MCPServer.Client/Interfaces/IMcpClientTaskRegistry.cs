using System.Text.Json;
using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Client.Interfaces;

public interface IMcpClientTaskRegistry
{
    event EventHandler<TaskStatusNotificationParams>? TaskStatusChanged;

    Fin<ListTasksResult> ListTasks(string? cursor);

    Fin<GetTaskResult> GetTask(string taskId);

    Fin<JsonElement> GetTaskResultPayload(string taskId);

    Fin<CancelTaskResult> CancelTask(string taskId);

    CreateTaskResult QueueTask(
        McpTaskMetadata? metadata,
        int? pollInterval,
        string? statusMessage,
        Func<CancellationToken, ValueTask<Fin<JsonElement>>> work);
}
