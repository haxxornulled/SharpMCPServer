using System.Text.Json.Serialization;
using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public sealed class McpToolDescriptor
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; init; }

    [JsonPropertyName("outputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? OutputSchema { get; init; }

    [JsonPropertyName("icons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpIcon[]? Icons { get; init; }

    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Annotations { get; init; }

    [JsonPropertyName("execution")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpToolExecution? Execution { get; init; }
}

public sealed class McpIcon
{
    [JsonPropertyName("src")]
    public string Src { get; init; } = string.Empty;

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }

    [JsonPropertyName("sizes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Sizes { get; init; }

    [JsonPropertyName("theme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Theme { get; init; }
}

public sealed class McpToolExecution
{
    [JsonPropertyName("taskSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaskSupport { get; init; }
}

public static class McpToolTaskSupport
{
    public const string Forbidden = "forbidden";
    public const string Optional = "optional";
    public const string Required = "required";

    public static bool IsValid(string? value)
    {
        return value is null or Forbidden or Optional or Required;
    }
}

public sealed class ToolsListResult
{
    [JsonPropertyName("tools")]
    public McpToolDescriptor[] Tools { get; init; } = Array.Empty<McpToolDescriptor>();

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}

public sealed class ToolsCallRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; init; }
}

public sealed class ToolCallResult
{
    [JsonPropertyName("content")]
    public ToolContent[] Content { get; init; } = Array.Empty<ToolContent>();

    [JsonPropertyName("isError")]
    public bool IsError { get; init; }

    [JsonPropertyName("structuredContent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? StructuredContent { get; init; }

    public static ToolCallResult Text(string text, bool isError = false, JsonElement? structuredContent = default)
    {
        return new ToolCallResult
        {
            Content =
            [
                new TextToolContent
                {
                    Text = text
                }
            ],
            IsError = isError,
            StructuredContent = structuredContent
        };
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextToolContent), "text")]
public abstract class ToolContent
{
}

public sealed class TextToolContent : ToolContent
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed class ServerInfoStructuredContent
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; init; } = string.Empty;

    [JsonPropertyName("implementationProfile")]
    public string ImplementationProfile { get; init; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public string[] Capabilities { get; init; } = Array.Empty<string>();
}
