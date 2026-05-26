using System.Collections.Concurrent;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp;

public sealed class McpTaskRegistry : IMcpTaskRegistry
{
    private readonly ConcurrentDictionary<string, TaskRecord> _tasks = new(StringComparer.Ordinal);
    private readonly IMcpTaskStatusNotifier _taskStatusNotifier;

    public McpTaskRegistry(IMcpTaskStatusNotifier taskStatusNotifier)
    {
        ArgumentNullException.ThrowIfNull(taskStatusNotifier);
        _taskStatusNotifier = taskStatusNotifier;
        SeedServerInfoTask();
    }

    public Fin<ListTasksResult> ListTasks(string? cursor)
    {
        var tasks = _tasks.Values
            .OrderByDescending(static record => record.Task.LastUpdatedAt, StringComparer.Ordinal)
            .Select(static record => record.Task)
            .ToArray();

        return Fin.Succ(new ListTasksResult
        {
            Tasks = tasks,
            NextCursor = default
        });
    }

    public Fin<GetTaskResult> GetTask(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var record))
        {
            return Fin.Fail<GetTaskResult>(Error.New($"Task '{taskId}' was not found."));
        }

        return Fin.Succ(ToGetTaskResult(record.Task));
    }

    public Fin<JsonElement> GetTaskResultPayload(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var record))
        {
            return Fin.Fail<JsonElement>(Error.New($"Task '{taskId}' was not found."));
        }

        if (record.ResultPayload is not { } payload)
        {
            return Fin.Fail<JsonElement>(Error.New($"Task '{taskId}' does not have a result payload."));
        }

        return Fin.Succ(payload.Clone());
    }

    public Fin<CancelTaskResult> CancelTask(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var record))
        {
            return Fin.Fail<CancelTaskResult>(Error.New($"Task '{taskId}' was not found."));
        }

        var now = DateTimeOffset.UtcNow;
        var cancelled = new McpTask
        {
            TaskId = record.Task.TaskId,
            Status = record.Task.Status is McpTaskStatuses.Completed or McpTaskStatuses.Failed
                ? record.Task.Status
                : McpTaskStatuses.Cancelled,
            StatusMessage = record.Task.Status is McpTaskStatuses.Completed or McpTaskStatuses.Failed
                ? record.Task.StatusMessage
                : "Task was cancelled via tasks/cancel.",
            CreatedAt = record.Task.CreatedAt,
            LastUpdatedAt = now.ToString("O"),
            Ttl = record.Task.Ttl,
            PollInterval = record.Task.PollInterval
        };

        _tasks[taskId] = new TaskRecord(cancelled, record.ResultPayload);
        _taskStatusNotifier.Publish(ToTaskStatusNotification(cancelled));
        return Fin.Succ(ToCancelTaskResult(cancelled));
    }

    private void SeedServerInfoTask()
    {
        var now = DateTimeOffset.UtcNow;
        using var contentDocument = JsonDocument.Parse("""
        {
          "content": [
            { "type": "text", "text": "Synthetic server.info task result." }
          ],
          "isError": false,
          "structuredContent": {
            "name": "MCPServer",
            "protocolVersion": "2025-11-25",
            "implementationProfile": "stdio-baseline-2025-11-25",
            "capabilities": ["tasks/list","tasks/get","tasks/result","tasks/cancel"]
          }
        }
        """);

        var task = new McpTask
        {
            TaskId = "server-info-bootstrap",
            Status = McpTaskStatuses.Completed,
            StatusMessage = "Synthetic bootstrap task for MCP task handlers.",
            CreatedAt = now.AddMinutes(-5).ToString("O"),
            LastUpdatedAt = now.ToString("O"),
            Ttl = null,
            PollInterval = 1000
        };

        _tasks[task.TaskId] = new TaskRecord(task, contentDocument.RootElement.Clone());
        _taskStatusNotifier.Publish(ToTaskStatusNotification(task));
    }

    private static GetTaskResult ToGetTaskResult(McpTask task)
    {
        return new GetTaskResult
        {
            TaskId = task.TaskId,
            Status = task.Status,
            StatusMessage = task.StatusMessage,
            CreatedAt = task.CreatedAt,
            LastUpdatedAt = task.LastUpdatedAt,
            Ttl = task.Ttl,
            PollInterval = task.PollInterval
        };
    }

    private static CancelTaskResult ToCancelTaskResult(McpTask task)
    {
        return new CancelTaskResult
        {
            TaskId = task.TaskId,
            Status = task.Status,
            StatusMessage = task.StatusMessage,
            CreatedAt = task.CreatedAt,
            LastUpdatedAt = task.LastUpdatedAt,
            Ttl = task.Ttl,
            PollInterval = task.PollInterval
        };
    }

    private static TaskStatusNotificationParams ToTaskStatusNotification(McpTask task)
    {
        return new TaskStatusNotificationParams
        {
            TaskId = task.TaskId,
            Status = task.Status,
            StatusMessage = task.StatusMessage,
            CreatedAt = task.CreatedAt,
            LastUpdatedAt = task.LastUpdatedAt,
            Ttl = task.Ttl,
            PollInterval = task.PollInterval
        };
    }

    private sealed record TaskRecord(McpTask Task, JsonElement? ResultPayload);
}
