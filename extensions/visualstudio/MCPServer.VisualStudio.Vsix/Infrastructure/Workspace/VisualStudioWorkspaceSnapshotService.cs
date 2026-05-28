namespace MCPServer.VisualStudio.Vsix.Infrastructure.Workspace;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using LanguageExt;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using MCPServer.VisualStudio.Workspace;

/// <summary>
/// Captures workspace state from the active Visual Studio solution and project graph.
/// </summary>
internal sealed class VisualStudioWorkspaceSnapshotService : IWorkspaceSnapshotService
{
    private readonly DTE2 _dte;
    private readonly JoinableTaskFactory _joinableTaskFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualStudioWorkspaceSnapshotService"/> class.
    /// </summary>
    /// <param name="dte">The Visual Studio automation object.</param>
    /// <param name="joinableTaskFactory">The package joinable task factory.</param>
    public VisualStudioWorkspaceSnapshotService(DTE2 dte, JoinableTaskFactory joinableTaskFactory)
    {
        _dte = dte ?? throw new ArgumentNullException(nameof(dte));
        _joinableTaskFactory = joinableTaskFactory ?? throw new ArgumentNullException(nameof(joinableTaskFactory));
    }

    /// <inheritdoc />
    public async ValueTask<Fin<WorkspaceSnapshot>> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var solution = _dte.Solution;
            var solutionBuild = solution?.SolutionBuild;
            var activeConfigurationObject = solutionBuild?.ActiveConfiguration;
            var solutionPath = solution?.FullName;
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                solutionPath = null;
            }
            var activeConfiguration = activeConfigurationObject?.Name;
            var activePlatform = (activeConfigurationObject as SolutionConfiguration2)?.PlatformName;
            var projectPaths = CollectProjectPaths(solution).ToArray();

            return Fin<WorkspaceSnapshot>.Succ(WorkspaceSnapshotComposer.Create(
                solutionPath,
                activeConfiguration,
                activePlatform,
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

    private static IEnumerable<string> CollectProjectPaths(Solution? solution)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (solution?.Projects == null)
        {
            yield break;
        }

        foreach (Project project in solution.Projects)
        {
            foreach (var projectPath in CollectProjectPaths(project))
            {
                yield return projectPath;
            }
        }
    }

    private static IEnumerable<string> CollectProjectPaths(Project? project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (project == null)
        {
            yield break;
        }

        string? kind = null;
        string? fullName = null;
        ProjectItems? projectItems = null;

        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            kind = project.Kind;
            fullName = project.FullName;
            projectItems = project.ProjectItems;
        }
        catch
        {
            yield break;
        }

        if (string.Equals(kind, ProjectKinds.vsProjectKindSolutionFolder, StringComparison.OrdinalIgnoreCase))
        {
            if (projectItems == null)
            {
                yield break;
            }

            foreach (ProjectItem item in projectItems)
            {
                Project? subProject = null;

                try
                {
                    subProject = item.SubProject;
                }
                catch
                {
                    // Ignore non-project items and continue walking the solution tree.
                }

                foreach (var projectPath in CollectProjectPaths(subProject))
                {
                    yield return projectPath;
                }
            }

            yield break;
        }

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            yield return fullName;
        }
    }
}
