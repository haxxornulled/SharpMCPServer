namespace MCPServer.Client;

public sealed class McpClientProtocolException : Exception
{
    public McpClientProtocolException(string message)
        : base(message)
    {
    }

    public McpClientProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
