using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCPServer.Domain.Mcp;

public sealed class CompleteRequestParams
{
    [JsonPropertyName("ref")]
    public JsonElement Ref { get; init; }

    [JsonPropertyName("argument")]
    public CompletionArgument? Argument { get; init; }

    [JsonPropertyName("context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompletionContext? Context { get; init; }
}

public sealed class CompletionArgument
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;
}

public sealed class CompletionContext
{
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Arguments { get; init; }
}

public sealed class CompleteResult
{
    [JsonPropertyName("completion")]
    public CompletionResultPayload Completion { get; init; } = new CompletionResultPayload();

    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Meta { get; init; }
}

public sealed class CompletionResultPayload
{
    [JsonPropertyName("values")]
    public string[] Values { get; init; } = System.Array.Empty<string>();

    [JsonPropertyName("total")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Total { get; init; }

    [JsonPropertyName("hasMore")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HasMore { get; init; }
}

public readonly struct McpCompletionReference
{
    private McpCompletionReference(string type, string name, string uri)
    {
        Type = type;
        Name = name;
        Uri = uri;
    }

    public string Type { get; }

    public string Name { get; }

    public string Uri { get; }

    public bool IsPrompt => string.Equals(Type, McpCompletionReferenceTypes.Prompt, StringComparison.Ordinal);

    public bool IsResource => string.Equals(Type, McpCompletionReferenceTypes.Resource, StringComparison.Ordinal);

    public static McpCompletionReference Prompt(string name)
    {
        return new McpCompletionReference(McpCompletionReferenceTypes.Prompt, name, string.Empty);
    }

    public static McpCompletionReference Resource(string uri)
    {
        return new McpCompletionReference(McpCompletionReferenceTypes.Resource, string.Empty, uri);
    }
}

public static class McpCompletionReferenceTypes
{
    public const string Prompt = "ref/prompt";
    public const string Resource = "ref/resource";
}
