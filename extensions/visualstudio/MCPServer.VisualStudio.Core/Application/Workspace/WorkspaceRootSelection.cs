namespace MCPServer.VisualStudio.Workspace;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Describes the active workspace root selected from Visual Studio project query.
/// </summary>
internal sealed class WorkspaceRootSelection
{
    public string WorkspaceRoot { get; init; } = string.Empty;

    public string WorkspaceRootSource { get; init; } = "unavailable";

    public string? PrimaryProjectPath { get; init; }

    public bool HasWorkspaceRoot => !string.IsNullOrWhiteSpace(this.WorkspaceRoot);
}

/// <summary>
/// Resolves a workspace root from Visual Studio solution and project paths.
/// </summary>
internal static class WorkspaceRootResolver
{
    public static WorkspaceRootSelection Resolve(string? solutionPath, IReadOnlyCollection<string> projectPaths)
    {
        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            var normalizedSolutionPath = solutionPath!;
            var solutionDirectory = GetDirectoryName(normalizedSolutionPath);
            return new WorkspaceRootSelection
            {
                WorkspaceRoot = solutionDirectory,
                WorkspaceRootSource = "solution",
                PrimaryProjectPath = projectPaths.FirstOrDefault(),
            };
        }

        var normalizedProjectDirectories = projectPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(GetDirectoryName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedProjectDirectories.Length > 0)
        {
            var commonRoot = GetCommonDirectoryPath(normalizedProjectDirectories);
            var workspaceRoot = normalizedProjectDirectories[0];

            if (commonRoot is { Length: > 0 } && !IsRootOnly(commonRoot))
            {
                workspaceRoot = commonRoot;
            }

            return new WorkspaceRootSelection
            {
                WorkspaceRoot = workspaceRoot,
                WorkspaceRootSource = "project",
                PrimaryProjectPath = projectPaths.FirstOrDefault(),
            };
        }

        return new WorkspaceRootSelection();
    }

    private static string GetDirectoryName(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.GetDirectoryName(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? fullPath;
    }

    private static string? GetCommonDirectoryPath(IReadOnlyList<string> directoryPaths)
    {
        if (directoryPaths.Count == 0)
        {
            return null;
        }

        string? current = Path.GetFullPath(directoryPaths[0]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var index = 1; index < directoryPaths.Count; index++)
        {
            var candidate = Path.GetFullPath(directoryPaths[index]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var sharedPrefix = GetSharedPrefix(current!, candidate);
            if (string.IsNullOrWhiteSpace(sharedPrefix))
            {
                return null;
            }

            current = sharedPrefix;
        }

        return current!;
    }

    private static string? GetSharedPrefix(string first, string second)
    {
        var firstRoot = Path.GetPathRoot(first);
        var secondRoot = Path.GetPathRoot(second);
        if (string.IsNullOrWhiteSpace(firstRoot) || string.IsNullOrWhiteSpace(secondRoot))
        {
            return null;
        }

        if (!string.Equals(firstRoot, secondRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var firstSegments = GetSegments(first, firstRoot);
        var secondSegments = GetSegments(second, secondRoot);
        var sharedSegments = new List<string>();
        var limit = Math.Min(firstSegments.Length, secondSegments.Length);
        for (var index = 0; index < limit; index++)
        {
            if (!string.Equals(firstSegments[index], secondSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            sharedSegments.Add(firstSegments[index]);
        }

        if (sharedSegments.Count == 0)
        {
            return firstRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return sharedSegments.Aggregate(
            firstRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            (current, segment) => Path.Combine(current, segment));
    }

    private static string[] GetSegments(string path, string root)
    {
        var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(trimmedPath, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var relativePath = trimmedPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsRootOnly(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !string.IsNullOrWhiteSpace(root) && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase);
    }
}
