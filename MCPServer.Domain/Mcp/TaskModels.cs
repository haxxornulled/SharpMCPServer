using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCPServer.Domain.Mcp;

public static class McpTaskStatuses
{
    public const string Working = "working";
    public const string InputRequired = "input_required";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static bool IsValid(string? value)
    {
        return value is Working or InputRequired or Completed or Failed or Cancelled;
    }
}

public class McpTask
{
    [JsonPropertyName("taskId")]
    public string TaskId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = McpTaskStatuses.Working;

    [JsonPropertyName("statusMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StatusMessage { get; init; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("lastUpdatedAt")]
    public string LastUpdatedAt { get; init; } = string.Empty;

    [JsonPropertyName("ttl")]
    public long? Ttl { get; init; }

    [JsonPropertyName("pollInterval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PollInterval { get; init; }
}

public sealed class McpTaskMetadata
{
    [JsonPropertyName("ttl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Ttl { get; init; }
}

public sealed class McpRelatedTaskMetadata
{
    [JsonPropertyName("taskId")]
    public string TaskId { get; init; } = string.Empty;
}

public sealed class CreateTaskResult
{
    [JsonPropertyName("task")]
    public McpTask Task { get; init; } = new McpTask();
}

public sealed class GetTaskRequest
{
    [JsonPropertyName("taskId")]
    public string TaskId { get; init; } = string.Empty;
}

public sealed class GetTaskResult : McpTask
{
}

public sealed class GetTaskPayloadRequest
{
    [JsonPropertyName("taskId")]
    public string TaskId { get; init; } = string.Empty;
}

public sealed class GetTaskPayloadResult
{
    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Meta { get; init; }
}

public sealed class ListTasksResult
{
    [JsonPropertyName("tasks")]
    public McpTask[] Tasks { get; init; } = Array.Empty<McpTask>();

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}

public sealed class CancelTaskRequest
{
    [JsonPropertyName("taskId")]
    public string TaskId { get; init; } = string.Empty;
}

public sealed class CancelTaskResult : McpTask
{
}

public sealed class McpTasksCapability
{
    [JsonPropertyName("list")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? List { get; init; }

    [JsonPropertyName("cancel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Cancel { get; init; }

    [JsonPropertyName("requests")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpTaskRequestsCapability? Requests { get; init; }
}

public sealed class McpTaskRequestsCapability
{
    [JsonPropertyName("tools/call")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ToolsCall { get; init; }
}
