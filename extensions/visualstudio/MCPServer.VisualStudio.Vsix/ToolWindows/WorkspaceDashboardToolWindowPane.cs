namespace MCPServer.VisualStudio.Vsix.ToolWindows;

using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

/// <summary>
/// Hosts the MCPServer workspace dashboard in a Visual Studio tool window.
/// </summary>
[Guid(PackageGuids.WorkspaceDashboardToolWindowGuidString)]
public sealed class WorkspaceDashboardToolWindowPane : ToolWindowPane
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceDashboardToolWindowPane"/> class.
    /// </summary>
    /// <param name="viewModel">The dashboard view model supplied by the package.</param>
    public WorkspaceDashboardToolWindowPane(WorkspaceDashboardViewModel viewModel)
    {
        Caption = "MCPServer Workspace";
        Content = new WorkspaceDashboardControl
        {
            DataContext = viewModel,
        };
    }
}
