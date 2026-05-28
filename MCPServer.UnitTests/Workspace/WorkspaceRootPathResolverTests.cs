using LanguageExt;
using LanguageExt.Common;
using MCPServer.Workspace.Configuration;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;
using MCPServer.Workspace.Services;
using Xunit;

namespace MCPServer.UnitTests.Workspace;

public sealed class WorkspaceRootPathResolverTests
{
    [Fact]
    public void ResolveWorkspaceRootPath_Finds_Repository_Marker_Above_Nested_Path()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-workspace-root-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "src", "inner");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(root, "MCPServer.slnx"), string.Empty);

        try
        {
            var resolved = WorkspaceRootPathResolver.ResolveWorkspaceRootPath(nested);

            Assert.Equal(Path.GetFullPath(root), resolved);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DefaultWorkspaceRootCatalog_Uses_Detected_Checkout_Root_When_No_Roots_Are_Configured()
    {
        var options = new McpWorkspaceOptions();
        var sandboxCatalog = new EmptyWorkspaceSandboxCatalog();
        var rootCatalog = new DefaultWorkspaceRootCatalog(options, sandboxCatalog);

        var roots = rootCatalog.GetRoots();

        Assert.Single(roots);
        Assert.Equal("workspace", roots[0].Name);
        Assert.Equal("workspace", roots[0].Kind);
        Assert.True(roots[0].AllowWrite);
        Assert.Equal(WorkspaceRootPathResolver.ResolveDefaultWorkspaceRootPath(), roots[0].Path);
    }

    private sealed class EmptyWorkspaceSandboxCatalog : IWorkspaceSandboxCatalog
    {
        public IReadOnlyList<WorkspaceRoot> GetSandboxes()
        {
            return [];
        }

        public Fin<WorkspaceRoot> FindSandbox(string name)
        {
            return Fin.Fail<WorkspaceRoot>(Error.New("No sandbox."));
        }
    }
}
