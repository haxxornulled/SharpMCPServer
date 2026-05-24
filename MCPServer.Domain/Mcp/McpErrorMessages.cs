namespace MCPServer.Domain.Mcp;

public static class McpErrorMessages
{
    public const string SessionNotInitialized = "The MCP session must be initialized before this method can be used.";
    public const string SessionNotReady = "The MCP session has not received notifications/initialized yet.";
    public const string SessionAlreadyInitialized = "The MCP session is already initialized.";
}
