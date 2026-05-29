using Autofac;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Tools.Workspace;
using MCPServer.Workspace.Configuration;
using System.Text.Json;
using Xunit;

namespace MCPServer.UnitTests.Workspace;

public sealed class WorkspaceToolsModuleTests
{
    [Fact]
    public void WorkspaceToolsModule_Registers_Workspace_Tools()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "MCPServer.Workspace.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var builder = new ContainerBuilder();
        builder.RegisterInstance(new McpWorkspaceOptions
        {
            ApprovalToken = "approved",
            Roots =
            [
                new McpWorkspaceRootOptions
                {
                    Name = "workspace",
                    Path = rootPath,
                    AllowWrite = true
                }
            ]
        })
            .AsSelf()
            .SingleInstance();
        builder.RegisterModule(new WorkspaceToolsModule());

        using var container = builder.Build();

        Assert.NotNull(container.ResolveKeyed<IMcpTool>(WorkspaceToolNames.RootsList));
        Assert.NotNull(container.ResolveKeyed<IMcpTool>(WorkspaceToolNames.SandboxesList));
        Assert.NotNull(container.ResolveKeyed<IMcpTool>(WorkspaceToolNames.SandboxesCreate));
        Assert.NotNull(container.ResolveKeyed<IMcpTool>(WorkspaceToolNames.SandboxesDelete));
        Assert.NotNull(container.ResolveKeyed<IMcpTool>(WorkspaceToolNames.FilesRead));
        Assert.NotNull(container.ResolveKeyed<IMcpTool>(WorkspaceToolNames.FilesSearch));
        Assert.NotNull(container.ResolveKeyed<IMcpTool>(WorkspaceToolNames.FilesWrite));
        Assert.NotNull(container.ResolveKeyed<IMcpTool>(WorkspaceToolNames.FilesApplyPatch));

        var createDescriptor = container.ResolveKeyed<IMcpTool>(WorkspaceToolNames.SandboxesCreate).Descriptor;
        AssertToolSchemaDoesNotExposeApprovalToken(createDescriptor.InputSchema);

        var deleteDescriptor = container.ResolveKeyed<IMcpTool>(WorkspaceToolNames.SandboxesDelete).Descriptor;
        AssertToolSchemaDoesNotExposeApprovalToken(deleteDescriptor.InputSchema);

        Directory.Delete(rootPath, recursive: true);
    }

    private static void AssertToolSchemaDoesNotExposeApprovalToken(JsonElement inputSchema)
    {
        var properties = inputSchema.GetProperty("properties");
        Assert.False(properties.TryGetProperty("approvalToken", out _));
        var required = inputSchema.GetProperty("required").EnumerateArray().Select(element => element.GetString()).ToArray();
        Assert.DoesNotContain("approvalToken", required);
    }
}
