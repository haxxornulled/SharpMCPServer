namespace MCPServer.VisualStudio.Workspace;

using System.Threading;
using LanguageExt;

/// <summary>
/// Provides the current Visual Studio workspace snapshot for IDE-facing features.
/// </summary>
public interface IWorkspaceSnapshotService
{
    /// <summary>
    /// Captures the current workspace state from Visual Studio project query.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the query.</param>
    /// <returns>A recoverable result describing the current workspace state.</returns>
    ValueTask<Fin<WorkspaceSnapshot>> CaptureAsync(CancellationToken cancellationToken);
}
