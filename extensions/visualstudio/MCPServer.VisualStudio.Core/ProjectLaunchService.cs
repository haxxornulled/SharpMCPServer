namespace MCPServer.VisualStudio;

using System.Diagnostics;
using LanguageExt;
using MCPServer.VisualStudio.Workspace;

/// <summary>
/// Launches the repository chat console and host from the active Visual Studio workspace.
/// </summary>
public interface IProjectLaunchService
{
    /// <summary>
    /// Launches the chat console using the active workspace root.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the launch operation.</param>
    /// <returns>A recoverable result containing a human-readable launch message.</returns>
    ValueTask<Fin<string>> LaunchChatConsoleAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Launches the host using the active workspace root.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the launch operation.</param>
    /// <returns>A recoverable result containing a human-readable launch message.</returns>
    ValueTask<Fin<string>> LaunchHostAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Launches MCPServer projects from the active Visual Studio workspace.
/// </summary>
public sealed class ProjectLaunchService : IProjectLaunchService
{
    private const string ChatProjectPath = @"MCPServer.Client.Console\MCPServer.Client.Console.csproj";
    private const string HostProjectPath = @"MCPServer.Host\MCPServer.Host.csproj";
    private const string ChatLaunchProfile = "chat";
    private const string HostLaunchProfile = "host";
    private readonly IWorkspaceSnapshotService _workspaceSnapshotService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectLaunchService"/> class.
    /// </summary>
    /// <param name="workspaceSnapshotService">The workspace snapshot service.</param>
    public ProjectLaunchService(IWorkspaceSnapshotService workspaceSnapshotService)
    {
        _workspaceSnapshotService = workspaceSnapshotService;
    }

    /// <inheritdoc />
    public ValueTask<Fin<string>> LaunchChatConsoleAsync(CancellationToken cancellationToken)
    {
        return LaunchAsync(ChatProjectPath, ChatLaunchProfile, "chat console", cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<Fin<string>> LaunchHostAsync(CancellationToken cancellationToken)
    {
        return LaunchAsync(HostProjectPath, HostLaunchProfile, "host", cancellationToken);
    }

    private async ValueTask<Fin<string>> LaunchAsync(
        string projectPath,
        string launchProfile,
        string displayName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workspaceSnapshot = await _workspaceSnapshotService.CaptureAsync(cancellationToken).ConfigureAwait(false);
        return workspaceSnapshot.Match(
            Succ: snapshot => StartProcess(snapshot.WorkspaceRoot, projectPath, launchProfile, displayName, cancellationToken),
            Fail: error => Fin<string>.Fail(error));
    }

    private static Fin<string> StartProcess(
        string repoRoot,
        string projectPath,
        string launchProfile,
        string displayName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var fullProjectPath = GetFullProjectPath(repoRoot, projectPath);
            if (!File.Exists(fullProjectPath))
            {
                return Fin<string>.Fail($"The project file '{fullProjectPath}' could not be found.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardError = false,
                RedirectStandardOutput = false,
            };

#if NET472
            startInfo.Arguments = BuildArguments(fullProjectPath, launchProfile);
#else
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(fullProjectPath);
            startInfo.ArgumentList.Add("--launch-profile");
            startInfo.ArgumentList.Add(launchProfile);
#endif

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Fin<string>.Fail($"Failed to launch the MCPServer {displayName}.");
            }

            return Fin<string>.Succ($"Launched the MCPServer {displayName} from '{repoRoot}' with launch profile '{launchProfile}'.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fin<string>.Fail($"Failed to launch the MCPServer {displayName}: {ex.Message}");
        }
    }

    private static string GetFullProjectPath(string repoRoot, string projectPath)
    {
        var combinedPath = Path.Combine(repoRoot, projectPath);
        return Path.GetFullPath(combinedPath);
    }

#if NET472
    private static string BuildArguments(string fullProjectPath, string launchProfile)
    {
        return string.Join(
            " ",
            "run",
            "--project",
            Quote(fullProjectPath),
            "--launch-profile",
            Quote(launchProfile));
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
#endif
}
