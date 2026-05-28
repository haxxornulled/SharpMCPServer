namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleWorkspaceContextResolver
{
    private static readonly string[] WorkspaceRootHintEnvironmentVariables =
    [
        "MCP_WORKSPACE_ROOT",
        "SolutionDir",
        "SolutionPath",
        "MSBuildProjectDirectory",
        "ProjectDir"
    ];

    public static string ResolveCheckoutRoot(ConsoleOptions options, Func<string, string?>? getEnvironmentVariable = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var explicitRoot = ConsoleOptions.NormalizeWorkspaceRoot(options.WorkspaceRoot);
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return explicitRoot;
        }

        var environmentRoot = ResolveWorkspaceRootHint(getEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentRoot))
        {
            return environmentRoot;
        }

        return ResolveCheckoutRoot(Environment.CurrentDirectory);
    }

    public static string ResolveCheckoutRoot(string startDirectory)
    {
        var normalizedStartDirectory = ConsoleOptions.NormalizeWorkspaceRoot(startDirectory);
        if (string.IsNullOrWhiteSpace(normalizedStartDirectory))
        {
            return string.Empty;
        }

        var directory = new DirectoryInfo(normalizedStartDirectory);
        while (directory is not null)
        {
            if (LooksLikeCheckoutRoot(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return normalizedStartDirectory;
    }

    public static string? ResolveWorkspaceRootHint(Func<string, string?>? getEnvironmentVariable = null)
    {
        var environment = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

        foreach (var environmentVariableName in WorkspaceRootHintEnvironmentVariables)
        {
            var resolved = TryNormalizeWorkspaceRoot(environment(environmentVariableName));
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? TryNormalizeWorkspaceRoot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return ConsoleOptions.NormalizeWorkspaceRoot(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static bool LooksLikeCheckoutRoot(string directory)
    {
        try
        {
            if (File.Exists(Path.Combine(directory, "MCPServer.slnx")) ||
                Directory.EnumerateFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly).Any() ||
                Directory.EnumerateFiles(directory, "*.sln", SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }

            var gitPath = Path.Combine(directory, ".git");
            return File.Exists(gitPath) || Directory.Exists(gitPath);
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
}
