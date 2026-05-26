using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCPServer.Domain.Mcp;

public abstract class ElicitRequestParamsBase
{
    [JsonPropertyName("task")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpTaskMetadata? Task { get; init; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Meta { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed class ElicitRequestFormParams : ElicitRequestParamsBase
{
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; init; }

    [JsonPropertyName("requestedSchema")]
    public JsonElement RequestedSchema { get; init; }
}

public sealed class ElicitRequestUrlParams : ElicitRequestParamsBase
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "url";

    [JsonPropertyName("elicitationId")]
    public string ElicitationId { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;
}

public sealed class ElicitResult
{
    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Meta { get; init; }

    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Content { get; init; }
}
