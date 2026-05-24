using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public static class McpJsonElements
{
    public static JsonElement EmptyObject { get; } = CreateElement("{}");

    public static JsonElement EmptyArray { get; } = CreateElement("[]");

    private static JsonElement CreateElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
