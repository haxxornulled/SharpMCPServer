using System.Text.Json.Serialization;

namespace MCPServer.Domain.Mcp;

public sealed class CursorRequestParams
{
    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; init; }
}
