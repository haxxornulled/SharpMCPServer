using Autofac;
using MCPServer.Workspace.Configuration;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Services;
using MCPServer.Workspace.Stores;

namespace MCPServer.Workspace;

public sealed class WorkspaceModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterType<SqliteWorkspaceSandboxRegistry>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DefaultWorkspaceSandboxManager>()
            .As<IWorkspaceSandboxCatalog>()
            .As<IWorkspaceSandboxManager>()
            .SingleInstance();

        builder.RegisterType<DefaultWorkspaceRootCatalog>()
            .As<IWorkspaceRootCatalog>()
            .SingleInstance();

        builder.RegisterType<DefaultWorkspacePathResolver>()
            .As<IWorkspacePathResolver>()
            .SingleInstance();

        builder.RegisterType<WorkspacePatchApplier>()
            .As<IWorkspacePatchApplier>()
            .SingleInstance();

        builder.RegisterType<DefaultWorkspaceFileService>()
            .As<IWorkspaceFileService>()
            .SingleInstance();
    }
}
