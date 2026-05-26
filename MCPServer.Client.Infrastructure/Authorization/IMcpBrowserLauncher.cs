namespace MCPServer.Client.Infrastructure.Authorization;

public interface IMcpBrowserLauncher
{
    bool TryLaunch(Uri uri, out string? errorMessage);
}
