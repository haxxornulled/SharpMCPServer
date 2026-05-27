using Autofac;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Workspace;
using MCPServer.Tools.Workspace.Tools;

namespace MCPServer.Tools.Workspace;

public sealed class WorkspaceToolsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterModule(new WorkspaceModule());

        RegisterTool<WorkspaceRootsListTool>(builder, WorkspaceToolNames.RootsList);
        RegisterTool<WorkspaceSandboxesListTool>(builder, WorkspaceToolNames.SandboxesList);
        RegisterTool<WorkspaceSandboxCreateTool>(builder, WorkspaceToolNames.SandboxesCreate);
        RegisterTool<WorkspaceSandboxDeleteTool>(builder, WorkspaceToolNames.SandboxesDelete);
        RegisterTool<WorkspaceFileReadTool>(builder, WorkspaceToolNames.FilesRead);
        RegisterTool<WorkspaceFileSearchTool>(builder, WorkspaceToolNames.FilesSearch);
        RegisterTool<WorkspaceFileWriteTool>(builder, WorkspaceToolNames.FilesWrite);
        RegisterTool<WorkspaceFileApplyPatchTool>(builder, WorkspaceToolNames.FilesApplyPatch);
    }

    private static void RegisterTool<TTool>(ContainerBuilder builder, string name)
        where TTool : IMcpTool
    {
        builder.RegisterType<TTool>()
            .Keyed<IMcpTool>(name)
            .As<IMcpTool>()
            .SingleInstance();
    }
}
