using System.Text.Json.Serialization;

namespace MCPServer.Domain.Mcp;

public sealed class McpRoot
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }
}

public sealed class RootsListResult
{
    [JsonPropertyName("roots")]
    public McpRoot[] Roots { get; init; } = [];
}
