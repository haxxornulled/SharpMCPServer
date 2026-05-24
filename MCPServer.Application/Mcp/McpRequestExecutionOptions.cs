namespace MCPServer.Application.Mcp;

public sealed class McpRequestExecutionOptions
{
    public TimeSpan? DefaultRequestTimeout { get; init; } = TimeSpan.FromSeconds(60);
}
