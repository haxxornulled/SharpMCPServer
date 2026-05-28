namespace MCPServer.VisualStudio.Extensibility.Tests;

using MCPServer.VisualStudio.Workspace;
using Xunit;

public sealed class WorkspaceSnapshotComposerTests
{
    [Fact]
    public void Create_UsesSolutionMetadataAndProjectCountToBuildSnapshot()
    {
        var capturedAtUtc = new DateTime(2026, 05, 28, 14, 30, 00, DateTimeKind.Utc);
        var projectPaths = new[]
        {
            @"C:\Repos\MCPServer\MCPServer.Host\MCPServer.Host.csproj",
            @"C:\Repos\MCPServer\MCPServer.Client.Console\MCPServer.Client.Console.csproj",
        };

        var snapshot = WorkspaceSnapshotComposer.Create(
            @"C:\Repos\MCPServer\MCPServer.slnx",
            "Debug",
            "Any CPU",
            projectPaths,
            capturedAtUtc);

        Assert.Equal(@"C:\Repos\MCPServer", snapshot.WorkspaceRoot);
        Assert.Equal("solution", snapshot.WorkspaceRootSource);
        Assert.Equal(@"C:\Repos\MCPServer\MCPServer.slnx", snapshot.SolutionPath);
        Assert.Equal("MCPServer.slnx", snapshot.SolutionName);
        Assert.Equal(projectPaths[0], snapshot.PrimaryProjectPath);
        Assert.Equal("Debug", snapshot.ActiveConfiguration);
        Assert.Equal("Any CPU", snapshot.ActivePlatform);
        Assert.Equal(2, snapshot.ProjectCount);
        Assert.Equal(capturedAtUtc, snapshot.CapturedAtUtc);
        Assert.Equal("Visual Studio workspace snapshot captured from the active solution.", snapshot.StatusMessage);
    }

    [Fact]
    public void Create_UsesSingularProjectStatusWhenOnlyOneProjectIsLoaded()
    {
        var capturedAtUtc = new DateTime(2026, 05, 28, 14, 35, 00, DateTimeKind.Utc);
        var projectPaths = new[]
        {
            @"C:\Repos\MCPServer\src\ProjectA\ProjectA.csproj",
        };

        var snapshot = WorkspaceSnapshotComposer.Create(
            null,
            null,
            null,
            projectPaths,
            capturedAtUtc);

        Assert.Equal(@"C:\Repos\MCPServer\src\ProjectA", snapshot.WorkspaceRoot);
        Assert.Equal("project", snapshot.WorkspaceRootSource);
        Assert.Null(snapshot.SolutionPath);
        Assert.Null(snapshot.SolutionName);
        Assert.Equal(projectPaths[0], snapshot.PrimaryProjectPath);
        Assert.Equal(1, snapshot.ProjectCount);
        Assert.Equal(capturedAtUtc, snapshot.CapturedAtUtc);
        Assert.Equal("Visual Studio workspace snapshot captured from the loaded project.", snapshot.StatusMessage);
    }

    [Fact]
    public void Create_UsesPluralProjectStatusWhenMultipleProjectsAreLoaded()
    {
        var capturedAtUtc = new DateTime(2026, 05, 28, 14, 40, 00, DateTimeKind.Utc);
        var projectPaths = new[]
        {
            @"C:\Repos\MCPServer\src\ProjectA\ProjectA.csproj",
            @"C:\Repos\MCPServer\src\ProjectB\ProjectB.csproj",
        };

        var snapshot = WorkspaceSnapshotComposer.Create(
            null,
            null,
            null,
            projectPaths,
            capturedAtUtc);

        Assert.Equal(@"C:\Repos\MCPServer\src", snapshot.WorkspaceRoot);
        Assert.Equal("project", snapshot.WorkspaceRootSource);
        Assert.Equal(projectPaths[0], snapshot.PrimaryProjectPath);
        Assert.Equal(2, snapshot.ProjectCount);
        Assert.Equal(capturedAtUtc, snapshot.CapturedAtUtc);
        Assert.Equal("Visual Studio workspace snapshot captured from the loaded project set.", snapshot.StatusMessage);
    }

    [Fact]
    public void Create_FallsBackWhenNoSolutionOrProjectsAreAvailable()
    {
        var capturedAtUtc = new DateTime(2026, 05, 28, 14, 45, 00, DateTimeKind.Utc);

        var snapshot = WorkspaceSnapshotComposer.Create(
            null,
            null,
            null,
            Array.Empty<string>(),
            capturedAtUtc);

        Assert.Empty(snapshot.WorkspaceRoot);
        Assert.Equal("unavailable", snapshot.WorkspaceRootSource);
        Assert.Null(snapshot.SolutionPath);
        Assert.Null(snapshot.SolutionName);
        Assert.Null(snapshot.PrimaryProjectPath);
        Assert.Equal(0, snapshot.ProjectCount);
        Assert.Equal(capturedAtUtc, snapshot.CapturedAtUtc);
        Assert.Equal("Open a solution or project in Visual Studio to populate the workspace dashboard.", snapshot.StatusMessage);
    }
}
