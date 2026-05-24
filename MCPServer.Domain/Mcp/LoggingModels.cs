using System.Text.Json.Serialization;
using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public static class McpLogLevels
{
    public const string Debug = "debug";
    public const string Info = "info";
    public const string Notice = "notice";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";
    public const string Alert = "alert";
    public const string Emergency = "emergency";

    public static bool IsValid(string? level)
    {
        return GetSeverity(level) >= 0;
    }

    public static int GetSeverity(string? level)
    {
        return level switch
        {
            Emergency => 0,
            Alert => 1,
            Critical => 2,
            Error => 3,
            Warning => 4,
            Notice => 5,
            Info => 6,
            Debug => 7,
            _ => -1
        };
    }
}

public sealed class LoggingSetLevelRequest
{
    [JsonPropertyName("level")]
    public string Level { get; init; } = string.Empty;
}

public sealed class LoggingMessageNotificationParams
{
    [JsonPropertyName("level")]
    public string Level { get; init; } = string.Empty;

    [JsonPropertyName("logger")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Logger { get; init; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; init; }
}
