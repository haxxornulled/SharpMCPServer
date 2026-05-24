using System.Text.Json.Serialization;
using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public sealed class McpPromptDescriptor
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpPromptArgument[]? Arguments { get; init; }

    [JsonPropertyName("icons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpIcon[]? Icons { get; init; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Meta { get; init; }
}

public sealed class McpPromptArgument
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Required { get; init; }
}

public sealed class PromptsListResult
{
    [JsonPropertyName("prompts")]
    public McpPromptDescriptor[] Prompts { get; init; } = System.Array.Empty<McpPromptDescriptor>();

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }
}

public sealed class PromptsGetRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; init; }
}

public sealed class PromptsGetResult
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("messages")]
    public PromptMessage[] Messages { get; init; } = System.Array.Empty<PromptMessage>();
}

public sealed class PromptMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = McpRoles.User;

    [JsonPropertyName("content")]
    public PromptContent Content { get; init; } = new TextPromptContent();
}

public static class McpRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";

    public static bool IsValid(string role)
    {
        return role is User or Assistant;
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextPromptContent), "text")]
public abstract class PromptContent
{
    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Annotations { get; init; }

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Meta { get; init; }
}

public sealed class TextPromptContent : PromptContent
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}
