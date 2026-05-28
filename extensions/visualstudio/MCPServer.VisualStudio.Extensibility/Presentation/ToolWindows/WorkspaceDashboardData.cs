namespace MCPServer.VisualStudio.Extensibility.ToolWindows;

using System.Globalization;
using System.Runtime.Serialization;
using LanguageExt;
using MCPServer.VisualStudio.Extensibility;
using MCPServer.VisualStudio.Extensibility.Workspace;
using Microsoft.VisualStudio.Extensibility.UI;

/// <summary>
/// Remote UI data context for the workspace dashboard.
/// </summary>
[DataContract]
internal sealed class WorkspaceDashboardData : NotifyPropertyChangedObject
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
    /// Initializes a new instance of the <see cref="WorkspaceDashboardData"/> class.
    /// </summary>
    /// <param name="workspaceSnapshotService">The workspace snapshot service.</param>
    /// <param name="projectLaunchService">The project launch service.</param>
    public WorkspaceDashboardData(
        IWorkspaceSnapshotService workspaceSnapshotService,
        IProjectLaunchService projectLaunchService)
    {
        _workspaceSnapshotService = workspaceSnapshotService;
        _projectLaunchService = projectLaunchService;

        RefreshCommand = new AsyncCommand((_, _, cancellationToken) => RefreshAsync(cancellationToken));
        OpenChatConsoleCommand = new AsyncCommand((_, _, cancellationToken) => LaunchChatConsoleAsync(cancellationToken));
        OpenHostCommand = new AsyncCommand((_, _, cancellationToken) => LaunchHostAsync(cancellationToken));
    }

    /// <summary>
    /// Gets the command that refreshes the workspace snapshot.
    /// </summary>
    [DataMember]
    public AsyncCommand RefreshCommand { get; }

    /// <summary>
    /// Gets the command that launches the chat console.
    /// </summary>
    [DataMember]
    public AsyncCommand OpenChatConsoleCommand { get; }

    /// <summary>
    /// Gets the command that launches the MCP host.
    /// </summary>
    [DataMember]
    public AsyncCommand OpenHostCommand { get; }

    /// <summary>
    /// Gets or sets the active workspace root.
    /// </summary>
    [DataMember]
    public string WorkspaceRoot
    {
        get => _workspaceRoot;
        private set => SetProperty(ref _workspaceRoot, value);
    }

    /// <summary>
    /// Gets or sets the source used to resolve the workspace root.
    /// </summary>
    [DataMember]
    public string WorkspaceRootSource
    {
        get => _workspaceRootSource;
        private set => SetProperty(ref _workspaceRootSource, value);
    }

    /// <summary>
    /// Gets or sets the solution path.
    /// </summary>
    [DataMember]
    public string SolutionPath
    {
        get => _solutionPath;
        private set => SetProperty(ref _solutionPath, value);
    }

    /// <summary>
    /// Gets or sets the solution file name.
    /// </summary>
    [DataMember]
    public string SolutionName
    {
        get => _solutionName;
        private set => SetProperty(ref _solutionName, value);
    }

    /// <summary>
    /// Gets or sets the primary project path.
    /// </summary>
    [DataMember]
    public string PrimaryProjectPath
    {
        get => _primaryProjectPath;
        private set => SetProperty(ref _primaryProjectPath, value);
    }

    /// <summary>
    /// Gets or sets the active configuration.
    /// </summary>
    [DataMember]
    public string ActiveConfiguration
    {
        get => _activeConfiguration;
        private set => SetProperty(ref _activeConfiguration, value);
    }

    /// <summary>
    /// Gets or sets the active platform.
    /// </summary>
    [DataMember]
    public string ActivePlatform
    {
        get => _activePlatform;
        private set => SetProperty(ref _activePlatform, value);
    }

    /// <summary>
    /// Gets or sets the project count.
    /// </summary>
    [DataMember]
    public int ProjectCount
    {
        get => _projectCount;
        private set => SetProperty(ref _projectCount, value);
    }

    /// <summary>
    /// Gets or sets the status message shown in the dashboard.
    /// </summary>
    [DataMember]
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// Gets or sets the last refresh time as a human-readable string.
    /// </summary>
    [DataMember]
    public string LastRefreshedText
    {
        get => _lastRefreshedText;
        private set => SetProperty(ref _lastRefreshedText, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the dashboard is currently refreshing.
    /// </summary>
    [DataMember]
    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    /// <summary>
    /// Refreshes the workspace snapshot from Visual Studio.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the refresh operation.</param>
    /// <returns>A task that completes when the refresh finishes.</returns>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        RefreshCommand.CanExecute = false;
        StatusMessage = "Refreshing the Visual Studio workspace snapshot...";

        try
        {
            var snapshot = await _workspaceSnapshotService.CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.IsFail)
            {
                ApplyRefreshFailure(snapshot.Match(Succ: static _ => string.Empty, Fail: error => error.Message));
            }
            else
            {
                ApplySnapshot(snapshot.Match(Succ: static value => value, Fail: static _ => throw new InvalidOperationException()));
            }
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
            RefreshCommand.CanExecute = true;
        }
    }

    private async Task LaunchChatConsoleAsync(CancellationToken cancellationToken)
    {
        await ExecuteLaunchAsync(
            token => _projectLaunchService.LaunchChatConsoleAsync(token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task LaunchHostAsync(CancellationToken cancellationToken)
    {
        await ExecuteLaunchAsync(
            token => _projectLaunchService.LaunchHostAsync(token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteLaunchAsync(Func<CancellationToken, ValueTask<Fin<string>>> launchAsync, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await launchAsync(cancellationToken).ConfigureAwait(false);
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
        RefreshCommand.CanExecute = true;
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
    {
        return string.IsNullOrWhiteSpace(value)
            ? "(not available)"
            : value;
    }
}
