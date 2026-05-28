namespace MCPServer.VisualStudio.Extensibility.Commands;

using System.Threading;
using System.Threading.Tasks;
using MCPServer.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

/// <summary>
/// Opens the workspace dashboard tool window.
/// </summary>
[VisualStudioContribution]
public sealed class OpenWorkspaceDashboardCommand : Command
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenWorkspaceDashboardCommand"/> class.
    /// </summary>
    /// <param name="extensibility">The Visual Studio extensibility host.</param>
    public OpenWorkspaceDashboardCommand(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
    }

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%MCPServer.VisualStudio.Extensibility.OpenWorkspaceDashboard.DisplayName%")
    {
        Placements = [CommandPlacement.KnownPlacements.ViewOtherWindowsMenu],
        Icon = new(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
        TooltipText = "%MCPServer.VisualStudio.Extensibility.OpenWorkspaceDashboard.ToolTipText%",
    };

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext _, CancellationToken cancellationToken)
    {
        try
        {
            await this.Extensibility.Shell().ShowToolWindowAsync<WorkspaceDashboardToolWindow>(activate: true, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await this.Extensibility.Shell().ShowPromptAsync(ex.Message, PromptOptions.OK, cancellationToken).ConfigureAwait(false);
        }
    }
}
