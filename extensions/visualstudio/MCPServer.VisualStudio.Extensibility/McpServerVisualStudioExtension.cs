namespace MCPServer.VisualStudio.Extensibility;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;
using MCPServer.VisualStudio.Extensibility.ToolWindows;
using MCPServer.VisualStudio.Extensibility.Workspace;

/// <summary>
/// Registers the Visual Studio extension services used by the MCPServer IDE integration.
/// </summary>
[VisualStudioContribution]
public sealed class McpServerVisualStudioExtension : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "MCPServer.VisualStudio.Extensibility.f2ccc5e1-7b02-4b1b-88b1-e1466048dc0e",
            version: this.ExtensionAssemblyVersion,
            publisherName: "haxxornulled",
            displayName: "MCPServer Visual Studio Extension",
            description: "Launches the MCPServer chat console and host from Visual Studio.")
    };

    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        serviceCollection.AddSingleton<IWorkspaceSnapshotService, VisualStudioWorkspaceSnapshotService>();
        serviceCollection.AddSingleton<IProjectLaunchService, ProjectLaunchService>();
        serviceCollection.AddTransient<WorkspaceDashboardData>();
        serviceCollection.AddTransient<WorkspaceDashboardContent>();
    }
}
