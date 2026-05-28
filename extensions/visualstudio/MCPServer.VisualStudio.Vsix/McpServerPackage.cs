namespace MCPServer.VisualStudio.Vsix;

using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using MCPServer.VisualStudio;
using MCPServer.VisualStudio.Vsix.Infrastructure.Workspace;
using MCPServer.VisualStudio.Vsix.ToolWindows;
using MCPServer.VisualStudio.Workspace;

/// <summary>
/// Composes the Visual Studio extension services and command surface for MCPServer.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("MCPServer Visual Studio Extension", "Launches the MCPServer chat console, host, and workspace dashboard from Visual Studio.", "1.0")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(WorkspaceDashboardToolWindowPane))]
[Guid(PackageGuids.PackageGuidString)]
public sealed class McpServerPackage : AsyncPackage
{
    private IServiceProvider? _serviceProvider;
    private IVsStatusbar? _statusBar;

    /// <inheritdoc />
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress).ConfigureAwait(false);
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = (DTE2?)await GetServiceAsync(typeof(DTE)).ConfigureAwait(false);
        if (dte is null)
        {
            throw new InvalidOperationException("Visual Studio automation services were not available.");
        }

        var services = new ServiceCollection();
        services.AddSingleton(JoinableTaskFactory);
        services.AddSingleton(dte);
        services.AddSingleton<IWorkspaceSnapshotService, VisualStudioWorkspaceSnapshotService>();
        services.AddSingleton<IProjectLaunchService, ProjectLaunchService>();
        services.AddTransient<WorkspaceDashboardViewModel>();

        _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        _statusBar = (IVsStatusbar?)await GetServiceAsync(typeof(SVsStatusbar)).ConfigureAwait(false);

        var commandService = (OleMenuCommandService?)await GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(false);
        if (commandService is null)
        {
            throw new InvalidOperationException("The Visual Studio menu command service was not available.");
        }

        RegisterCommands(commandService);
    }

    /// <inheritdoc />
    protected override async Task<object> InitializeToolWindowAsync(Type toolWindowType, int id, CancellationToken cancellationToken)
    {
        if (toolWindowType == typeof(WorkspaceDashboardToolWindowPane))
        {
            return GetRequiredService<WorkspaceDashboardViewModel>();
        }

        return await base.InitializeToolWindowAsync(toolWindowType, id, cancellationToken).ConfigureAwait(false);
    }

    private void RegisterCommands(OleMenuCommandService commandService)
    {
        commandService.AddCommand(CreateCommand(PackageCommandIds.OpenChatConsole, LaunchChatConsoleAsync));
        commandService.AddCommand(CreateCommand(PackageCommandIds.OpenHost, LaunchHostAsync));
        commandService.AddCommand(CreateCommand(PackageCommandIds.OpenWorkspaceDashboard, ShowWorkspaceDashboardAsync));
    }

    private OleMenuCommand CreateCommand(int commandId, Func<CancellationToken, Task> executeAsync)
    {
        var menuCommandId = new CommandID(new Guid(PackageGuids.CommandSetGuidString), commandId);
        return new OleMenuCommand(
            (_, __) => _ = ExecuteCommandAsync(executeAsync),
            menuCommandId);
    }

    private Task ExecuteCommandAsync(Func<CancellationToken, Task> executeAsync)
        => JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await executeAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await ShowFailureAsync(ex.Message, CancellationToken.None).ConfigureAwait(false);
            }
        }).Task;

    private async Task LaunchChatConsoleAsync(CancellationToken cancellationToken)
    {
        var result = await GetRequiredService<IProjectLaunchService>().LaunchChatConsoleAsync(cancellationToken).ConfigureAwait(false);
        await ReportLaunchResultAsync(result, "Launched the MCPServer chat console.", cancellationToken).ConfigureAwait(false);
    }

    private async Task LaunchHostAsync(CancellationToken cancellationToken)
    {
        var result = await GetRequiredService<IProjectLaunchService>().LaunchHostAsync(cancellationToken).ConfigureAwait(false);
        await ReportLaunchResultAsync(result, "Launched the MCPServer host.", cancellationToken).ConfigureAwait(false);
    }

    private async Task ShowWorkspaceDashboardAsync(CancellationToken cancellationToken)
    {
        await ShowToolWindowAsync(typeof(WorkspaceDashboardToolWindowPane), 0, true, cancellationToken).ConfigureAwait(false);
        await SetStatusBarTextAsync("Opened the MCPServer workspace dashboard.", cancellationToken).ConfigureAwait(false);
    }

    private async Task ReportLaunchResultAsync(Fin<string> result, string successMessage, CancellationToken cancellationToken)
    {
        if (result.IsFail)
        {
            await ShowFailureAsync(result.Match(Succ: static _ => string.Empty, Fail: error => error.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        await SetStatusBarTextAsync(successMessage, cancellationToken).ConfigureAwait(false);
    }

    private async Task ShowFailureAsync(string message, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        VsShellUtilities.ShowMessageBox(
            this,
            message,
            "MCPServer",
            OLEMSGICON.OLEMSGICON_CRITICAL,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private async Task SetStatusBarTextAsync(string message, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        if (_statusBar is not null)
        {
            _statusBar.SetText(message);
        }
    }

    private T GetRequiredService<T>()
        where T : class
    {
        if (_serviceProvider is null)
        {
            throw new InvalidOperationException("The Visual Studio extension services are not initialized.");
        }

        return _serviceProvider.GetService(typeof(T)) as T
            ?? throw new InvalidOperationException($"The Visual Studio service '{typeof(T).FullName}' was not registered.");
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            (_serviceProvider as IDisposable)?.Dispose();
        }

        base.Dispose(disposing);
    }
}
