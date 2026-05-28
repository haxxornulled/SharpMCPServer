namespace MCPServer.VisualStudio.Extensibility.Tests;

using MCPServer.VisualStudio.Workspace;
using Xunit;

public sealed class WorkspaceRootResolverTests
{
    [Fact]
    public void Resolve_UsesSolutionDirectoryWhenSolutionPathIsPresent()
    {
        var solutionPath = @"C:\Repos\MCPServer\MCPServer.slnx";
        var projectPaths = new[]
        {
            @"C:\Repos\MCPServer\MCPServer.Host\MCPServer.Host.csproj",
            @"C:\Repos\MCPServer\MCPServer.Client.Console\MCPServer.Client.Console.csproj",
        };

        var selection = WorkspaceRootResolver.Resolve(solutionPath, projectPaths);

        Assert.Equal(@"C:\Repos\MCPServer", selection.WorkspaceRoot);
        Assert.Equal("solution", selection.WorkspaceRootSource);
        Assert.Equal(projectPaths[0], selection.PrimaryProjectPath);
        Assert.True(selection.HasWorkspaceRoot);
    }

    [Fact]
    public void Resolve_UsesCommonProjectAncestorWhenSolutionPathIsMissing()
    {
        var projectPaths = new[]
        {
            @"C:\Repos\MCPServer\src\ProjectA\ProjectA.csproj",
            @"C:\Repos\MCPServer\src\ProjectB\ProjectB.csproj",
        };

        var selection = WorkspaceRootResolver.Resolve(null, projectPaths);

        Assert.Equal(@"C:\Repos\MCPServer\src", selection.WorkspaceRoot);
        Assert.Equal("project", selection.WorkspaceRootSource);
        Assert.Equal(projectPaths[0], selection.PrimaryProjectPath);
        Assert.True(selection.HasWorkspaceRoot);
    }

    [Fact]
    public void Resolve_FallsBackToFirstProjectDirectoryWhenNoSharedAncestorExists()
    {
        var projectPaths = new[]
        {
            @"C:\Repos\MCPServer\src\ProjectA\ProjectA.csproj",
            @"D:\Repos\AnotherRepo\src\ProjectB\ProjectB.csproj",
        };

        var selection = WorkspaceRootResolver.Resolve(null, projectPaths);

        Assert.Equal(@"C:\Repos\MCPServer\src\ProjectA", selection.WorkspaceRoot);
        Assert.Equal("project", selection.WorkspaceRootSource);
        Assert.Equal(projectPaths[0], selection.PrimaryProjectPath);
        Assert.True(selection.HasWorkspaceRoot);
    }
}
