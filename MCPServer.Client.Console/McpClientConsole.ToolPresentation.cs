using MCPServer.Domain.Mcp;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleToolPresentation
{
    public static void PrintTools(ToolsListResult tools)
    {
        Console.WriteLine("Tools exposed by server:");
        foreach (var tool in tools.Tools)
        {
            Console.WriteLine($"- {tool.Name}: {tool.Description}");
        }
    }

    public static string GetToolName(ConsoleOptions options)
    {
        return string.IsNullOrWhiteSpace(options.ToolName) ? "server.info" : options.ToolName;
    }
}
