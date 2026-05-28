namespace MCPServer.VisualStudio.Extensibility.ToolWindows;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

/// <summary>
/// Displays the active Visual Studio workspace state and launch actions.
/// </summary>
[VisualStudioContribution]
internal sealed class WorkspaceDashboardToolWindow : ToolWindow
{
    private readonly WorkspaceDashboardContent _content;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceDashboardToolWindow"/> class.
    /// </summary>
    /// <param name="extensibility">The Visual Studio extensibility host.</param>
    /// <param name="content">The dashboard content resolved from dependency injection.</param>
    public WorkspaceDashboardToolWindow(
        VisualStudioExtensibility extensibility,
        WorkspaceDashboardContent content)
        : base(extensibility)
    {
        Title = "MCPServer Workspace";
        _content = content;
    }

    /// <inheritdoc />
    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
    };

    /// <inheritdoc />
    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
        => Task.FromResult<IRemoteUserControl>(_content);

    /// <inheritdoc />
    public override Task InitializeAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content.Dispose();
        }

        base.Dispose(disposing);
    }
}
