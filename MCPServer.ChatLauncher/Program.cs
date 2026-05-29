using System.ComponentModel;
using System.Diagnostics;

namespace MCPServer.ChatLauncher;

/// <summary>
/// Entry point for the repository-owned MCPServer chat launcher.
/// </summary>
public static class Program
{
    /// <summary>
    /// Launches the already-built MCPServer chat console with a short, readable terminal banner.
    /// </summary>
    /// <param name="args">Command-line arguments supplied by the caller.</param>
    /// <returns>A process exit code compatible with shell launchers.</returns>
    public static async Task<int> Main(string[] args)
    {
        if (!ChatLauncherArguments.TryParse(args, out var options, out var parseError))
        {
            Console.Error.WriteLine(parseError);
            Console.Error.WriteLine();
            Console.Error.WriteLine(ChatLauncherText.HelpText);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(ChatLauncherText.HelpText);
            return 0;
        }

        var repositoryRoot = RepositoryLayout.ResolveRepositoryRoot();
        var consoleDll = RepositoryLayout.GetConsoleOutputPath(repositoryRoot);
        var hostDll = RepositoryLayout.GetHostOutputPath(repositoryRoot);
        var missingArtifacts = RepositoryLayout.GetMissingArtifacts(consoleDll, hostDll);

        if (missingArtifacts.Count > 0)
        {
            WriteMissingBuildOutputs(repositoryRoot, missingArtifacts);
            return 1;
        }

        Console.WriteLine("MCPServer chat console launcher");
        Console.WriteLine($"Repo root: {repositoryRoot}");
        Console.WriteLine($"Provider:  {GetProviderLabel(options.Provider)}");
        Console.WriteLine("Launching the already-built console...");

        try
        {
            return await ChatConsoleProcess.RunAsync(repositoryRoot, consoleDll, hostDll, options.Provider, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            Console.Error.WriteLine($"Failed to start the MCPServer chat console: {ex.Message}");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Prints a short, junior-dev-friendly message explaining which build outputs are missing.
    /// </summary>
    /// <param name="repositoryRoot">The resolved repository root.</param>
    /// <param name="missingArtifacts">The missing artifact paths.</param>
    private static void WriteMissingBuildOutputs(string repositoryRoot, IReadOnlyCollection<string> missingArtifacts)
    {
        Console.WriteLine("MCPServer chat console could not start because the build outputs are missing.");
        Console.WriteLine("Build the solution first:");
        Console.WriteLine("  dotnet build .\\MCPServer.slnx -c Debug");
        Console.WriteLine("Missing:");

        foreach (var missingArtifact in missingArtifacts)
        {
            Console.WriteLine($"  - {missingArtifact}");
        }

        Console.WriteLine();
        Console.WriteLine($"Repo root: {repositoryRoot}");
    }

    private static string GetProviderLabel(string? provider)
    {
        return string.IsNullOrWhiteSpace(provider)
            ? "router default"
            : provider;
    }
}

/// <summary>
/// Parses the tiny argument surface for the chat launcher.
/// </summary>
public static class ChatLauncherArguments
{
    /// <summary>
    /// Attempts to parse launcher arguments.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="options">The parsed options when parsing succeeds.</param>
    /// <param name="error">The validation error when parsing fails.</param>
    /// <returns><see langword="true" /> when parsing succeeds; otherwise, <see langword="false" />.</returns>
    public static bool TryParse(string[] args, out ChatLauncherOptions options, out string? error)
    {
        var provider = string.Empty;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;
                case "--provider":
                    if (index + 1 >= args.Length)
                    {
                        options = default!;
                        error = "Missing value for --provider.";
                        return false;
                    }

                    provider = args[++index];
                    break;
                default:
                    options = default!;
                    error = $"Unknown argument: {arg}";
                    return false;
            }
        }

        options = new ChatLauncherOptions(provider, showHelp);
        error = null;
        return true;
    }
}

/// <summary>
/// Represents the small set of options supported by the chat launcher.
/// </summary>
public sealed record ChatLauncherOptions(string Provider, bool ShowHelp);

/// <summary>
/// Resolves repository-relative file system paths for the launcher.
/// </summary>
public static class RepositoryLayout
{
    /// <summary>
    /// Resolves the repository root by walking upward from the launcher base directory until a repo marker is found.
    /// </summary>
    /// <param name="baseDirectory">
    /// Optional override used by tests. When omitted, the launcher base directory is used.
    /// </param>
    /// <returns>The absolute repository root path.</returns>
    public static string ResolveRepositoryRoot(string? baseDirectory = null)
    {
        var currentDirectory = new DirectoryInfo(baseDirectory ?? AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            if (ContainsRepositoryMarker(currentDirectory))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
    }

    /// <summary>
    /// Builds the absolute path to the already-built chat console assembly.
    /// </summary>
    /// <param name="repositoryRoot">The repository root path.</param>
    /// <returns>The expected Debug output path for the chat console.</returns>
    public static string GetConsoleOutputPath(string repositoryRoot)
        => Path.Combine(repositoryRoot, "MCPServer.Client.Console", "bin", "Debug", "net10.0", "MCPServer.Client.Console.dll");

    /// <summary>
    /// Builds the absolute path to the already-built host assembly.
    /// </summary>
    /// <param name="repositoryRoot">The repository root path.</param>
    /// <returns>The expected Debug output path for the host.</returns>
    public static string GetHostOutputPath(string repositoryRoot)
        => Path.Combine(repositoryRoot, "MCPServer.Host", "bin", "Debug", "net10.0", "MCPServer.Host.dll");

    /// <summary>
    /// Gets the missing build outputs for the launcher.
    /// </summary>
    /// <param name="consoleDll">The expected console assembly path.</param>
    /// <param name="hostDll">The expected host assembly path.</param>
    /// <returns>The missing file paths, if any.</returns>
    public static IReadOnlyList<string> GetMissingArtifacts(string consoleDll, string hostDll)
    {
        var missing = new List<string>(capacity: 2);
        if (!File.Exists(consoleDll))
        {
            missing.Add(consoleDll);
        }

        if (!File.Exists(hostDll))
        {
            missing.Add(hostDll);
        }

        return missing;
    }

    /// <summary>
    /// Determines whether the supplied directory contains a repository marker file or folder.
    /// </summary>
    /// <param name="directory">The directory to inspect.</param>
    /// <returns><see langword="true" /> when a repo marker is present; otherwise, <see langword="false" />.</returns>
    private static bool ContainsRepositoryMarker(DirectoryInfo directory)
        => directory.EnumerateFileSystemInfos(".git").Any()
            || directory.EnumerateFileSystemInfos("MCPServer.slnx").Any()
            || directory.EnumerateFileSystemInfos("MCPServer.sln").Any();
}

/// <summary>
/// Starts the already-built chat console process.
/// </summary>
public static class ChatConsoleProcess
{
    /// <summary>
    /// Launches the MCPServer chat console with the repository-owned host path and provider selection.
    /// </summary>
    /// <param name="repositoryRoot">The repository root used as the working directory.</param>
    /// <param name="consoleDll">The already-built client console assembly.</param>
    /// <param name="hostDll">The already-built host assembly.</param>
    /// <param name="provider">The inference provider identifier.</param>
    /// <param name="cancellationToken">The cancellation token for the wait operation.</param>
    /// <returns>The launched process exit code.</returns>
    public static async Task<int> RunAsync(
        string repositoryRoot,
        string consoleDll,
        string hostDll,
        string provider,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(consoleDll);
        startInfo.ArgumentList.Add("--transport");
        startInfo.ArgumentList.Add("stdio");
        startInfo.ArgumentList.Add("--server-path");
        startInfo.ArgumentList.Add("dotnet");
        startInfo.ArgumentList.Add("--server-arg");
        startInfo.ArgumentList.Add(hostDll);
        startInfo.ArgumentList.Add("--working-directory");
        startInfo.ArgumentList.Add(Path.GetDirectoryName(hostDll) ?? repositoryRoot);
        startInfo.ArgumentList.Add("--chat");

        if (!string.IsNullOrWhiteSpace(provider))
        {
            startInfo.ArgumentList.Add("--provider");
            startInfo.ArgumentList.Add(provider);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the MCPServer chat console process.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}

/// <summary>
/// Provides the chat launcher help text.
/// </summary>
public static class ChatLauncherText
{
    /// <summary>
    /// Gets the launcher help text shown to the user.
    /// </summary>
    public static string HelpText => """
    Usage:
      MCPServer.ChatLauncher [--provider <id>] [--help]

    Options:
      --provider <id>   Optional inference provider shortcut passed to the chat console.
      --help            Show this help.

    The launcher prints a small banner, checks the already-built Debug outputs,
    and then starts the repository-owned MCPServer chat console. If --provider is
    omitted, the chat console starts in router-default mode.
    """;
}
