using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp;

public sealed class NoOpTaskStatusNotifier : IMcpTaskStatusNotifier
{
    public void Publish(TaskStatusNotificationParams taskStatus)
    {
    }
}
