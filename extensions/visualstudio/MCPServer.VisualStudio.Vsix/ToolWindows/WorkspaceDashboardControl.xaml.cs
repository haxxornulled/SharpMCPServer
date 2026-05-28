namespace MCPServer.VisualStudio.Vsix.ToolWindows;

using System.Threading;
using System.Windows;
using System.Windows.Controls;

/// <summary>
/// WPF content for the workspace dashboard tool window.
/// </summary>
public partial class WorkspaceDashboardControl : UserControl
{
    private bool _hasRefreshed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceDashboardControl"/> class.
    /// </summary>
    public WorkspaceDashboardControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasRefreshed)
        {
            return;
        }

        _hasRefreshed = true;

        if (DataContext is not WorkspaceDashboardViewModel viewModel)
        {
            return;
        }

        _ = viewModel.RefreshAsync(CancellationToken.None);
    }
}
