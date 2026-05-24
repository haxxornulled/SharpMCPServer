using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp;

public sealed class McpLoggingState : IMcpLoggingState
{
    private string _minimumLevel = McpLogLevels.Info;

    public string MinimumLevel => Volatile.Read(ref _minimumLevel);

    public bool TrySetMinimumLevel(string level)
    {
        if (!McpLogLevels.IsValid(level))
        {
            return false;
        }

        Volatile.Write(ref _minimumLevel, level);
        return true;
    }

    public bool IsEnabled(string level)
    {
        var candidateSeverity = McpLogLevels.GetSeverity(level);
        if (candidateSeverity < 0)
        {
            return false;
        }

        var minimumSeverity = McpLogLevels.GetSeverity(MinimumLevel);
        return candidateSeverity <= minimumSeverity;
    }
}
