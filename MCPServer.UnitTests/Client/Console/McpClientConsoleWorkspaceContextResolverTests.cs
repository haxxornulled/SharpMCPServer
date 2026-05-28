using MCPServer.Client.ConsoleApp;
using Xunit;

namespace MCPServer.UnitTests.Client.Console;

public sealed class McpClientConsoleWorkspaceContextResolverTests
{
    [Fact]
    public void ResolveWorkspaceRootHint_Normalizes_SolutionPath()
    {
        var solutionPath = Path.Combine(Path.GetTempPath(), "workspace-root-test", "Demo.slnx");

        var resolved = McpClientConsoleWorkspaceContextResolver.ResolveWorkspaceRootHint(name =>
            string.Equals(name, "SolutionPath", StringComparison.Ordinal)
                ? solutionPath
                : null);

        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(solutionPath)), resolved);
    }

    [Fact]
    public void BuildStdioServerArguments_Uses_Environment_WorkspaceRoot_Hint()
    {
        var workspaceRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"mcpserver-workspace-root-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(workspaceRoot);

        var options = ConsoleOptions.Parse([
            "--server-path",
            "dotnet",
            "--server-arg",
            "MCPServer.Host.dll",
            "--working-directory",
            "."
        ]);

        var arguments = McpClientConsoleSessionComposition.BuildStdioServerArguments(
            options,
            name => string.Equals(name, "MCP_WORKSPACE_ROOT", StringComparison.Ordinal)
                ? workspaceRoot
                : null);

        Assert.Equal("MCPServer.Host.dll", arguments[0]);
        Assert.Equal("--McpWorkspace:Roots:0:Name=workspace", arguments[1]);
        Assert.Equal("--McpWorkspace:Roots:0:Path=" + workspaceRoot, arguments[2]);
        Assert.Equal("--McpWorkspace:Roots:0:AllowWrite=true", arguments[3]);
    }

}
