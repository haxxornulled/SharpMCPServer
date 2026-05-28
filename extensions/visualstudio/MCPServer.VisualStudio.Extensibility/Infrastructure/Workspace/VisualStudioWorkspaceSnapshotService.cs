namespace MCPServer.VisualStudio.Extensibility.Workspace;

using LanguageExt;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.ProjectSystem.Query;

/// <summary>
/// Captures workspace state from Visual Studio project query.
/// </summary>
internal sealed class VisualStudioWorkspaceSnapshotService : IWorkspaceSnapshotService
{
    private readonly VisualStudioExtensibility _extensibility;

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualStudioWorkspaceSnapshotService"/> class.
    /// </summary>
    /// <param name="extensibility">The Visual Studio extensibility host.</param>
    public VisualStudioWorkspaceSnapshotService(VisualStudioExtensibility extensibility)
    {
        _extensibility = extensibility;
    }

    /// <inheritdoc />
    public async ValueTask<Fin<WorkspaceSnapshot>> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var workspace = _extensibility.Workspaces();

            var solutionResults = await workspace.QuerySolutionAsync(
                solution => solution.With(s => new { s.Path, s.ActiveConfiguration, s.ActivePlatform }),
                cancellationToken).ConfigureAwait(false);

            var solution = solutionResults.FirstOrDefault();

            var projectResults = await workspace.QueryProjectsAsync(
                project => project.With(p => new { p.Path }),
                cancellationToken).ConfigureAwait(false);

            var projectPaths = projectResults
                .Select(project => project.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .ToArray();

            return Fin<WorkspaceSnapshot>.Succ(WorkspaceSnapshotComposer.Create(
                solution?.Path,
                solution?.ActiveConfiguration,
                solution?.ActivePlatform,
                projectPaths,
                DateTime.UtcNow));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fin<WorkspaceSnapshot>.Fail($"Unable to query the Visual Studio workspace: {ex.Message}");
        }
    }
}
