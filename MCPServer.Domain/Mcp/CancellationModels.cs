using System.Text.Json.Serialization;
using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public sealed class CancelledNotificationParams
{
    [JsonPropertyName("requestId")]
    public JsonElement RequestId { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
}
