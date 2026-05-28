namespace MCPServer.VisualStudio.Extensibility.Tests;

using LanguageExt;
using MCPServer.VisualStudio;
using MCPServer.VisualStudio.Workspace;
using Xunit;

public sealed class ProjectLaunchServiceTests
{
    [Fact]
    public async Task LaunchChatConsoleAsync_ReturnsFailureWhenWorkspaceSnapshotFails()
    {
        var service = new ProjectLaunchService(new FailingWorkspaceSnapshotService("workspace unavailable"));

        var result = await service.LaunchChatConsoleAsync(CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Contains("workspace unavailable", result.Match(Succ: static _ => string.Empty, Fail: error => error.Message));
    }

    [Fact]
    public async Task LaunchChatConsoleAsync_ReturnsFailureWhenProjectFileIsMissing()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var service = new ProjectLaunchService(new SuccessfulWorkspaceSnapshotService(workspaceRoot));

        var result = await service.LaunchChatConsoleAsync(CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Contains("could not be found", result.Match(Succ: static _ => string.Empty, Fail: error => error.Message));
    }

    private sealed class FailingWorkspaceSnapshotService : IWorkspaceSnapshotService
    {
        private readonly string _message;

        public FailingWorkspaceSnapshotService(string message)
        {
            _message = message;
        }

        public ValueTask<Fin<WorkspaceSnapshot>> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Fin<WorkspaceSnapshot>.Fail(_message));
        }
    }

    private sealed class SuccessfulWorkspaceSnapshotService : IWorkspaceSnapshotService
    {
        private readonly string _workspaceRoot;

        public SuccessfulWorkspaceSnapshotService(string workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
        }

        public ValueTask<Fin<WorkspaceSnapshot>> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Fin<WorkspaceSnapshot>.Succ(new WorkspaceSnapshot
            {
                WorkspaceRoot = _workspaceRoot,
                WorkspaceRootSource = "test",
                CapturedAtUtc = DateTime.UtcNow,
                ProjectCount = 0,
                StatusMessage = "captured for test",
            }));
        }
    }
}
