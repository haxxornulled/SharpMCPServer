using MCPServer.Domain.Mcp;

namespace MCPServer.Client.Interfaces;

public interface IMcpTaskStatusObserver
{
    ValueTask OnTaskStatusAsync(TaskStatusNotificationParams taskStatus, CancellationToken cancellationToken);
}
