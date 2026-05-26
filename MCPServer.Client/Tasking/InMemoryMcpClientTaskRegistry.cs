using System.Collections.Concurrent;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Client.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Client.Tasking;

public sealed class InMemoryMcpClientTaskRegistry : IMcpClientTaskRegistry
{
    private readonly ConcurrentDictionary<string, TaskRecord> _tasks = new(StringComparer.Ordinal);

    public event EventHandler<TaskStatusNotificationParams>? TaskStatusChanged;

    public Fin<ListTasksResult> ListTasks(string? cursor)
    {
        var tasks = _tasks.Values
            .Select(static record => record.Task)
            .OrderByDescending(static task => task.LastUpdatedAt, StringComparer.Ordinal)
            .ToArray();

        return Fin.Succ(new ListTasksResult
        {
            Tasks = tasks,
            NextCursor = null
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

        record.Cancellation.Cancel();
        var now = DateTimeOffset.UtcNow;
        var cancelled = CreateTask(
            taskId,
            McpTaskStatuses.Cancelled,
            "Task was cancelled by the MCP server.",
            record.Task.CreatedAt,
            now,
            record.Task.Ttl,
            record.Task.PollInterval);

        _tasks[taskId] = new TaskRecord(cancelled, record.ResultPayload, record.Cancellation);
        Publish(cancelled);
        return Fin.Succ(ToCancelTaskResult(cancelled));
    }

    public CreateTaskResult QueueTask(
        McpTaskMetadata? metadata,
        int? pollInterval,
        string? statusMessage,
        Func<CancellationToken, ValueTask<Fin<JsonElement>>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var now = DateTimeOffset.UtcNow;
        var taskId = Guid.NewGuid().ToString("N");
        var cancellation = new CancellationTokenSource();
        var task = CreateTask(
            taskId,
            McpTaskStatuses.Working,
            statusMessage ?? "Client task is running.",
            now.ToString("O"),
            now,
            metadata?.Ttl,
            pollInterval);

        _tasks[taskId] = new TaskRecord(task, null, cancellation);
        Publish(task);

        _ = RunTaskAsync(taskId, work, cancellation.Token);

        return new CreateTaskResult
        {
            Task = task
        };
    }

    private async Task RunTaskAsync(string taskId, Func<CancellationToken, ValueTask<Fin<JsonElement>>> work, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await work(cancellationToken).ConfigureAwait(false);
            if (!_tasks.TryGetValue(taskId, out var record))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (outcome.IsFail)
            {
                var failed = CreateTask(
                    taskId,
                    McpTaskStatuses.Failed,
                    outcome.Match(Succ: _ => string.Empty, Fail: error => error.Message),
                    record.Task.CreatedAt,
                    now,
                    record.Task.Ttl,
                    record.Task.PollInterval);

                _tasks[taskId] = new TaskRecord(failed, record.ResultPayload, record.Cancellation);
                Publish(failed);
                return;
            }

            var payload = outcome.Match(Succ: value => value.Clone(), Fail: _ => default);
            var completed = CreateTask(
                taskId,
                McpTaskStatuses.Completed,
                "Client task completed successfully.",
                record.Task.CreatedAt,
                now,
                record.Task.Ttl,
                record.Task.PollInterval);

            _tasks[taskId] = new TaskRecord(completed, payload, record.Cancellation);
            Publish(completed);
        }
        catch (OperationCanceledException)
        {
            if (!_tasks.TryGetValue(taskId, out var record))
            {
                return;
            }

            var cancelled = CreateTask(
                taskId,
                McpTaskStatuses.Cancelled,
                "Client task was cancelled.",
                record.Task.CreatedAt,
                DateTimeOffset.UtcNow,
                record.Task.Ttl,
                record.Task.PollInterval);

            _tasks[taskId] = new TaskRecord(cancelled, record.ResultPayload, record.Cancellation);
            Publish(cancelled);
        }
    }

    private void Publish(McpTask task)
    {
        var handler = TaskStatusChanged;
        if (handler is null)
        {
            return;
        }

        handler(this, new TaskStatusNotificationParams
        {
            TaskId = task.TaskId,
            Status = task.Status,
            StatusMessage = task.StatusMessage,
            CreatedAt = task.CreatedAt,
            LastUpdatedAt = task.LastUpdatedAt,
            Ttl = task.Ttl,
            PollInterval = task.PollInterval
        });
    }

    private static McpTask CreateTask(string taskId, string status, string? statusMessage, string createdAt, DateTimeOffset now, long? ttl, int? pollInterval)
    {
        return new McpTask
        {
            TaskId = taskId,
            Status = status,
            StatusMessage = statusMessage,
            CreatedAt = createdAt,
            LastUpdatedAt = now.ToString("O"),
            Ttl = ttl,
            PollInterval = pollInterval
        };
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

    private sealed record TaskRecord(McpTask Task, JsonElement? ResultPayload, CancellationTokenSource Cancellation);
}
