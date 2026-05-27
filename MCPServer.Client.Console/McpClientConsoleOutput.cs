using System.Text.Json;
using System.Text;
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

    public static string FormatToolResultForTranscript(ToolCallResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.IsError ? "Tool returned an error result." : "Tool returned a success result.");

        foreach (var content in result.Content)
        {
            if (content is TextToolContent text)
            {
                builder.AppendLine(text.Text);
            }
        }

        if (result.StructuredContent is { } structuredContent)
        {
            builder.AppendLine();
            builder.AppendLine("Structured content:");
            builder.AppendLine(JsonSerializer.Serialize(structuredContent, McpJsonSerializerContext.Default.JsonElement));
        }

        return builder.ToString().TrimEnd();
    }
}
