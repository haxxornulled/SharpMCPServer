using System.Text.Json.Serialization;

namespace MCPServer.Domain.Mcp;

public sealed class McpServerCapabilities
{
    [JsonPropertyName("tools")]
    public McpToolsCapability Tools { get; init; } = new McpToolsCapability();

    [JsonPropertyName("logging")]
    public McpLoggingCapability Logging { get; init; } = new McpLoggingCapability();

    [JsonPropertyName("resources")]
    public McpResourcesCapability Resources { get; init; } = new McpResourcesCapability();

    [JsonPropertyName("prompts")]
    public McpPromptsCapability Prompts { get; init; } = new McpPromptsCapability();

    [JsonPropertyName("completions")]
    public McpCompletionsCapability Completions { get; init; } = new McpCompletionsCapability();
}


public sealed class McpToolsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; init; }
}

public sealed class McpLoggingCapability
{
}

public sealed class McpResourcesCapability
{
    [JsonPropertyName("subscribe")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Subscribe { get; init; }

    [JsonPropertyName("listChanged")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ListChanged { get; init; }
}

public sealed class McpPromptsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; init; }
}

public sealed class McpCompletionsCapability
{
}
