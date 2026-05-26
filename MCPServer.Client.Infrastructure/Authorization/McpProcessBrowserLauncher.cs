using System.Diagnostics;

namespace MCPServer.Client.Infrastructure.Authorization;

public sealed class McpProcessBrowserLauncher : IMcpBrowserLauncher
{
    public bool TryLaunch(Uri uri, out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(uri);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                errorMessage = "Failed to launch the system browser.";
                return false;
            }

            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
