using MCPServer.Domain.Mcp;
namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpTaskStatusNotifier
{
    void Publish(TaskStatusNotificationParams taskStatus);
}
