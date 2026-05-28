namespace MCPServer.VisualStudio.Extensibility;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

/// <summary>
/// Opens the MCPServer chat console from Visual Studio.
/// </summary>
[VisualStudioContribution]
public sealed class OpenChatConsoleCommand : Command
{
    private readonly IProjectLaunchService _launchService;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenChatConsoleCommand"/> class.
    /// </summary>
    /// <param name="extensibility">The Visual Studio extensibility host.</param>
    /// <param name="launchService">The project launch service.</param>
    public OpenChatConsoleCommand(VisualStudioExtensibility extensibility, IProjectLaunchService launchService)
        : base(extensibility)
    {
        _launchService = launchService;
    }

    public override CommandConfiguration CommandConfiguration => new("%MCPServer.VisualStudio.Extensibility.OpenChatConsole.DisplayName%")
    {
        Placements = [CommandPlacement.KnownPlacements.ToolsMenu],
        Icon = new(ImageMoniker.KnownValues.Extension, IconSettings.IconAndText),
        TooltipText = "%MCPServer.VisualStudio.Extensibility.OpenChatConsole.ToolTipText%",
    };

    public override async Task ExecuteCommandAsync(IClientContext _, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _launchService.LaunchChatConsoleAsync(cancellationToken).ConfigureAwait(false);
            var message = result.Match(
                Succ: static value => value,
                Fail: error => error.Message);
            await this.Extensibility.Shell().ShowPromptAsync(message, PromptOptions.OK, cancellationToken).ConfigureAwait(false);
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
