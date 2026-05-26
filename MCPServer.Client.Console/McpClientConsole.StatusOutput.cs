using MCPServer.Domain.Mcp;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleStatusOutput
{
    public static void PrintConnectedMessage(InitializeResult initializeResult)
    {
        Console.WriteLine($"Connected to {initializeResult.ServerInfo.Name} {initializeResult.ServerInfo.Version} using MCP {initializeResult.ProtocolVersion}.");
    }

    public static void PrintCallingTool(string toolName)
    {
        Console.WriteLine();
        Console.WriteLine($"Calling tool: {toolName}");
    }
}
