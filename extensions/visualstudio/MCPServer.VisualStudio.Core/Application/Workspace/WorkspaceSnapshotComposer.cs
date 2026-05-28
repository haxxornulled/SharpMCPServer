namespace MCPServer.VisualStudio.Workspace;

/// <summary>
/// Composes a workspace snapshot from raw solution and project query results.
/// </summary>
public static class WorkspaceSnapshotComposer
{
    /// <summary>
    /// Creates a workspace snapshot from the supplied Visual Studio workspace data.
    /// </summary>
    /// <param name="solutionPath">The solution path reported by Visual Studio, if one is loaded.</param>
    /// <param name="activeConfiguration">The active build configuration reported by Visual Studio, if any.</param>
    /// <param name="activePlatform">The active build platform reported by Visual Studio, if any.</param>
    /// <param name="projectPaths">The project paths reported by Visual Studio.</param>
    /// <param name="capturedAtUtc">The UTC time when the snapshot was captured.</param>
    /// <returns>A composed workspace snapshot.</returns>
    public static WorkspaceSnapshot Create(
        string? solutionPath,
        string? activeConfiguration,
        string? activePlatform,
        IReadOnlyCollection<string> projectPaths,
        DateTime capturedAtUtc)
    {
        if (projectPaths is null)
        {
            throw new ArgumentNullException(nameof(projectPaths));
        }

        var selection = WorkspaceRootResolver.Resolve(solutionPath, projectPaths);
        return new WorkspaceSnapshot
        {
            WorkspaceRoot = selection.WorkspaceRoot,
            WorkspaceRootSource = selection.WorkspaceRootSource,
            SolutionPath = solutionPath,
            SolutionName = !string.IsNullOrWhiteSpace(solutionPath) ? Path.GetFileName(solutionPath) : null,
            PrimaryProjectPath = selection.PrimaryProjectPath,
            ActiveConfiguration = activeConfiguration,
            ActivePlatform = activePlatform,
            ProjectCount = projectPaths.Count,
            CapturedAtUtc = capturedAtUtc,
            StatusMessage = BuildStatusMessage(selection, solutionPath, projectPaths.Count),
        };
    }

    private static string BuildStatusMessage(WorkspaceRootSelection selection, string? solutionPath, int projectCount)
    {
        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            return projectCount == 0
                ? "Visual Studio reported an active solution and no loaded projects."
                : "Visual Studio workspace snapshot captured from the active solution.";
        }

        if (selection.HasWorkspaceRoot)
        {
            return projectCount == 1
                ? "Visual Studio workspace snapshot captured from the loaded project."
                : "Visual Studio workspace snapshot captured from the loaded project set.";
        }

        return "Open a solution or project in Visual Studio to populate the workspace dashboard.";
    }
}
