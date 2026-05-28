namespace MCPServer.VisualStudio.Extensibility.ToolWindows;

using Microsoft.VisualStudio.Extensibility.UI;

/// <summary>
/// Remote UI content for the workspace dashboard tool window.
/// </summary>
internal sealed class WorkspaceDashboardContent : RemoteUserControl
{
    private readonly WorkspaceDashboardData _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceDashboardContent"/> class.
    /// </summary>
    /// <param name="data">The remote UI data context.</param>
    public WorkspaceDashboardContent(WorkspaceDashboardData data)
        : base(dataContext: data)
    {
        _data = data;
    }

    /// <inheritdoc />
    public override async Task ControlLoadedAsync(CancellationToken cancellationToken)
    {
        await base.ControlLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _data.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
