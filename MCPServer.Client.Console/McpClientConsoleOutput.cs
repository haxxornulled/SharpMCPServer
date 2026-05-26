using System.Text.Json;
using MCPServer.Client.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleOutput
{
    public static void PrintToolResult(ToolCallResult result)
    {
        Console.WriteLine(result.IsError ? "Tool returned an error result." : "Tool returned a success result.");
        foreach (var content in result.Content)
        {
            if (content is TextToolContent text)
            {
                Console.WriteLine(text.Text);
            }
        }

        if (result.StructuredContent is { } structuredContent)
        {
            Console.WriteLine();
            Console.WriteLine("Structured content:");
            Console.WriteLine(JsonSerializer.Serialize(structuredContent, McpJsonSerializerContext.Default.JsonElement));
        }
    }
}
