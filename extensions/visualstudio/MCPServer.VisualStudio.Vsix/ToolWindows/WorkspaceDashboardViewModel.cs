namespace MCPServer.VisualStudio.Vsix.ToolWindows;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using LanguageExt;
using MCPServer.VisualStudio;
using MCPServer.VisualStudio.Workspace;

/// <summary>
/// View model for the Visual Studio workspace dashboard.
/// </summary>
public sealed class WorkspaceDashboardViewModel : BindableObject
{
    private readonly IProjectLaunchService _projectLaunchService;
    private readonly IWorkspaceSnapshotService _workspaceSnapshotService;
    private string _activeConfiguration = "(not available)";
    private string _activePlatform = "(not available)";
    private int _projectCount;
    private string _primaryProjectPath = "(not available)";
    private string _solutionName = "(not available)";
    private string _solutionPath = "(not available)";
    private string _statusMessage = "Loading workspace snapshot...";
    private string _workspaceRoot = "(not available)";
    private string _workspaceRootSource = "(not available)";
    private bool _isRefreshing;
    private string _lastRefreshedText = "(not refreshed yet)";

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceDashboardViewModel"/> class.
    /// </summary>
    /// <param name="workspaceSnapshotService">The workspace snapshot service.</param>
    /// <param name="projectLaunchService">The launch service for chat and host workflows.</param>
    public WorkspaceDashboardViewModel(
        IWorkspaceSnapshotService workspaceSnapshotService,
        IProjectLaunchService projectLaunchService)
    {
        _workspaceSnapshotService = workspaceSnapshotService ?? throw new ArgumentNullException(nameof(workspaceSnapshotService));
        _projectLaunchService = projectLaunchService ?? throw new ArgumentNullException(nameof(projectLaunchService));

        RefreshCommand = new AsyncCommand(RefreshAsync);
        OpenChatConsoleCommand = new AsyncCommand(LaunchChatConsoleAsync);
        OpenHostCommand = new AsyncCommand(LaunchHostAsync);
    }

    /// <summary>
    /// Gets the command that refreshes the workspace snapshot.
    /// </summary>
    public ICommand RefreshCommand { get; }

    /// <summary>
    /// Gets the command that launches the MCP chat console.
    /// </summary>
    public ICommand OpenChatConsoleCommand { get; }

    /// <summary>
    /// Gets the command that launches the MCP host.
    /// </summary>
    public ICommand OpenHostCommand { get; }

    /// <summary>
    /// Gets the active workspace root.
    /// </summary>
    public string WorkspaceRoot
    {
        get => _workspaceRoot;
        private set => SetProperty(ref _workspaceRoot, value);
    }

    /// <summary>
    /// Gets the source used to resolve the workspace root.
    /// </summary>
    public string WorkspaceRootSource
    {
        get => _workspaceRootSource;
        private set => SetProperty(ref _workspaceRootSource, value);
    }

    /// <summary>
    /// Gets the solution path.
    /// </summary>
    public string SolutionPath
    {
        get => _solutionPath;
        private set => SetProperty(ref _solutionPath, value);
    }

    /// <summary>
    /// Gets the solution file name.
    /// </summary>
    public string SolutionName
    {
        get => _solutionName;
        private set => SetProperty(ref _solutionName, value);
    }

    /// <summary>
    /// Gets the primary project path.
    /// </summary>
    public string PrimaryProjectPath
    {
        get => _primaryProjectPath;
        private set => SetProperty(ref _primaryProjectPath, value);
    }

    /// <summary>
    /// Gets the active build configuration.
    /// </summary>
    public string ActiveConfiguration
    {
        get => _activeConfiguration;
        private set => SetProperty(ref _activeConfiguration, value);
    }

    /// <summary>
    /// Gets the active build platform.
    /// </summary>
    public string ActivePlatform
    {
        get => _activePlatform;
        private set => SetProperty(ref _activePlatform, value);
    }

    /// <summary>
    /// Gets the current project count.
    /// </summary>
    public int ProjectCount
    {
        get => _projectCount;
        private set => SetProperty(ref _projectCount, value);
    }

    /// <summary>
    /// Gets the current status message.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// Gets the last refresh timestamp formatted for display.
    /// </summary>
    public string LastRefreshedText
    {
        get => _lastRefreshedText;
        private set => SetProperty(ref _lastRefreshedText, value);
    }

    /// <summary>
    /// Gets a value indicating whether a refresh operation is in progress.
    /// </summary>
    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    /// <summary>
    /// Refreshes the workspace snapshot from Visual Studio.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the refresh.</param>
    /// <returns>A task that completes when the refresh finishes.</returns>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        StatusMessage = "Refreshing the Visual Studio workspace snapshot...";

        try
        {
            var snapshot = await _workspaceSnapshotService.CaptureAsync(cancellationToken);
            if (snapshot.IsFail)
            {
                ApplyRefreshFailure(snapshot.Match(Succ: static _ => string.Empty, Fail: error => error.Message));
                return;
            }

            ApplySnapshot(snapshot.Match(Succ: static value => value, Fail: static _ => throw new InvalidOperationException()));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ApplyRefreshFailure(ex.Message);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Launches the MCP chat console from the active workspace.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the launch.</param>
    /// <returns>A task that completes when the launch finishes.</returns>
    public Task LaunchChatConsoleAsync(CancellationToken cancellationToken)
        => ExecuteLaunchAsync(
            token => _projectLaunchService.LaunchChatConsoleAsync(token),
            "Launching the MCPServer chat console...",
            cancellationToken);

    /// <summary>
    /// Launches the MCP host from the active workspace.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the launch.</param>
    /// <returns>A task that completes when the launch finishes.</returns>
    public Task LaunchHostAsync(CancellationToken cancellationToken)
        => ExecuteLaunchAsync(
            token => _projectLaunchService.LaunchHostAsync(token),
            "Launching the MCPServer host...",
            cancellationToken);

    private async Task ExecuteLaunchAsync(
        Func<CancellationToken, ValueTask<Fin<string>>> launchAsync,
        string pendingMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            StatusMessage = pendingMessage;
            var result = await launchAsync(cancellationToken);
            StatusMessage = result.Match(
                Succ: static message => message,
                Fail: error => error.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void ApplySnapshot(WorkspaceSnapshot snapshot)
    {
        WorkspaceRoot = FormatValue(snapshot.WorkspaceRoot);
        WorkspaceRootSource = FormatValue(snapshot.WorkspaceRootSource);
        SolutionPath = FormatValue(snapshot.SolutionPath);
        SolutionName = FormatValue(snapshot.SolutionName);
        PrimaryProjectPath = FormatValue(snapshot.PrimaryProjectPath);
        ActiveConfiguration = FormatValue(snapshot.ActiveConfiguration);
        ActivePlatform = FormatValue(snapshot.ActivePlatform);
        ProjectCount = snapshot.ProjectCount;
        StatusMessage = snapshot.StatusMessage;
        LastRefreshedText = snapshot.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture);
    }

    private void ApplyRefreshFailure(string message)
    {
        StatusMessage = $"Unable to refresh the dashboard: {message}";
        WorkspaceRoot = "(not available)";
        WorkspaceRootSource = "(not available)";
        SolutionPath = "(not available)";
        SolutionName = "(not available)";
        PrimaryProjectPath = "(not available)";
        ActiveConfiguration = "(not available)";
        ActivePlatform = "(not available)";
        ProjectCount = 0;
        LastRefreshedText = DateTime.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture);
    }

    private static string FormatValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "(not available)" : value!;
}
