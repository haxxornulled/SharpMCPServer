namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpLoggingState
{
    string MinimumLevel { get; }

    bool TrySetMinimumLevel(string level);

    bool IsEnabled(string level);
}
