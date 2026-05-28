namespace MCPServer.Workspace.Services;

internal static class WorkspaceRootPathResolver
{
    public static string ResolveDefaultWorkspaceRootPath()
    {
        return ResolveWorkspaceRootPath(AppContext.BaseDirectory);
    }

    internal static string ResolveWorkspaceRootPath(string? startDirectory)
    {
        var normalizedStartDirectory = NormalizePath(startDirectory);
        if (string.IsNullOrWhiteSpace(normalizedStartDirectory))
        {
            return string.Empty;
        }

        var directory = new DirectoryInfo(normalizedStartDirectory);
        while (directory is not null)
        {
            if (LooksLikeWorkspaceRoot(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return normalizedStartDirectory;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception) when (path is not null)
        {
            return path.Trim();
        }
    }

    private static bool LooksLikeWorkspaceRoot(string directory)
    {
        try
        {
            if (HasGitMetadata(directory))
            {
                return true;
            }

            return Directory.EnumerateFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly).Any() ||
                   Directory.EnumerateFiles(directory, "*.sln", SearchOption.TopDirectoryOnly).Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasGitMetadata(string directory)
    {
        var gitPath = Path.Combine(directory, ".git");
        return File.Exists(gitPath) || Directory.Exists(gitPath);
    }
}
