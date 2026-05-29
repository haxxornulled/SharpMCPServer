using System.Diagnostics;
using System.Globalization;
using MCPServer.Inference.Infrastructure.Options;

namespace MCPServer.Inference.Infrastructure.Hosting;

public static class LocalInferenceProviderProcessStartInfoFactory
{
    private const int DefaultLocalContextLength = 8192;
    private const int DefaultOllamaMaxLoadedModels = 1;
    private const int DefaultOllamaMaxQueue = 128;
    private const int DefaultOllamaNumParallel = 1;
    private const string DefaultOllamaKvCacheType = "q8_0";

    public static bool TryCreateOllamaStartInfo(
        string executablePath,
        Uri baseAddress,
        McpInferenceProviderOptions providerOptions,
        out ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(providerOptions);

        startInfo = CreateHiddenStartInfo(executablePath, WorkingDirectoryForExecutable(executablePath));
        startInfo.ArgumentList.Add("serve");

        var hostAndPort = GetHostAndPort(baseAddress, defaultPort: 11434);
        startInfo.Environment["OLLAMA_HOST"] = hostAndPort;
        startInfo.Environment["OLLAMA_FLASH_ATTENTION"] = "1";
        startInfo.Environment["OLLAMA_KV_CACHE_TYPE"] = DefaultOllamaKvCacheType;
        startInfo.Environment["OLLAMA_MAX_LOADED_MODELS"] = DefaultOllamaMaxLoadedModels.ToString(CultureInfo.InvariantCulture);
        startInfo.Environment["OLLAMA_MAX_QUEUE"] = DefaultOllamaMaxQueue.ToString(CultureInfo.InvariantCulture);
        startInfo.Environment["OLLAMA_NUM_PARALLEL"] = DefaultOllamaNumParallel.ToString(CultureInfo.InvariantCulture);

        var contextLength = providerOptions.ContextLength ?? DefaultLocalContextLength;
        if (contextLength > 0)
        {
            startInfo.Environment["OLLAMA_CONTEXT_LENGTH"] = contextLength.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(providerOptions.KeepAlive))
        {
            startInfo.Environment["OLLAMA_KEEP_ALIVE"] = providerOptions.KeepAlive.Trim();
        }

        return true;
    }

    public static bool TryCreateLmStudioServerStartInfo(
        string executablePath,
        Uri baseAddress,
        out ProcessStartInfo startInfo)
    {
        startInfo = CreateHiddenStartInfo(executablePath, WorkingDirectoryForExecutable(executablePath));
        startInfo.ArgumentList.Add("server");
        startInfo.ArgumentList.Add("start");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(GetPort(baseAddress, defaultPort: 1234).ToString(CultureInfo.InvariantCulture));
        return true;
    }

    public static bool TryCreateLmStudioLoadStartInfo(
        string executablePath,
        McpInferenceProviderOptions providerOptions,
        out ProcessStartInfo? startInfo)
    {
        startInfo = null;
        if (string.IsNullOrWhiteSpace(providerOptions.Model))
        {
            return false;
        }

        startInfo = CreateHiddenStartInfo(executablePath, WorkingDirectoryForExecutable(executablePath));
        startInfo.ArgumentList.Add("load");
        startInfo.ArgumentList.Add(providerOptions.Model.Trim());
        startInfo.ArgumentList.Add("--gpu");
        startInfo.ArgumentList.Add("max");
        startInfo.ArgumentList.Add("--context-length");
        startInfo.ArgumentList.Add((providerOptions.ContextLength ?? DefaultLocalContextLength).ToString(CultureInfo.InvariantCulture));
        return true;
    }

    public static bool TryCreateLmStudioListModelsStartInfo(
        string executablePath,
        out ProcessStartInfo startInfo)
    {
        startInfo = CreateHiddenStartInfo(executablePath, WorkingDirectoryForExecutable(executablePath));
        startInfo.ArgumentList.Add("ls");
        startInfo.ArgumentList.Add("--json");
        startInfo.ArgumentList.Add("--llm");
        return true;
    }

    public static bool TryCreateLmStudioStopStartInfo(
        string executablePath,
        out ProcessStartInfo startInfo)
    {
        startInfo = CreateHiddenStartInfo(executablePath, WorkingDirectoryForExecutable(executablePath));
        startInfo.ArgumentList.Add("server");
        startInfo.ArgumentList.Add("stop");
        return true;
    }

    public static string? ResolveOllamaExecutablePath()
    {
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ollama", "ollama.exe"));
            candidates.Add("ollama.exe");
            candidates.Add("ollama");
        }
        else
        {
            candidates.Add("/usr/local/bin/ollama");
            candidates.Add("/usr/bin/ollama");
            candidates.Add("ollama");
        }

        return ResolveExistingExecutable(candidates);
    }

    public static string? ResolveLmStudioExecutablePath()
    {
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(userProfile, ".lmstudio", "bin", "lms.exe"));
            candidates.Add(Path.Combine(userProfile, ".lmstudio", "bin", "lms"));
            candidates.Add(Path.Combine(localAppData, "Programs", "LM Studio", "resources", "app", "bin", "lms.exe"));
            candidates.Add(Path.Combine(localAppData, "Programs", "LM Studio", "resources", "app", "bin", "lms"));
            candidates.Add("lms.exe");
            candidates.Add("lms");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(home, ".lmstudio", "bin", "lms"));
            candidates.Add(Path.Combine(home, ".lmstudio", "bin", "lms.exe"));
            candidates.Add("lms");
        }

        return ResolveExistingExecutable(candidates);
    }

    private static ProcessStartInfo CreateHiddenStartInfo(string executablePath, string? workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        return new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
    }

    private static string WorkingDirectoryForExecutable(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        return string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory;
    }

    private static int GetPort(Uri baseAddress, int defaultPort)
    {
        if (baseAddress.IsDefaultPort || baseAddress.Port <= 0)
        {
            return defaultPort;
        }

        return baseAddress.Port;
    }

    private static string GetHostAndPort(Uri baseAddress, int defaultPort)
    {
        var host = baseAddress.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "127.0.0.1";
        }

        return $"{host}:{GetPort(baseAddress, defaultPort)}";
    }

    private static string? ResolveExistingExecutable(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (Path.IsPathFullyQualified(candidate))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                continue;
            }

            var pathCandidate = ResolveOnPath(candidate);
            if (!string.IsNullOrWhiteSpace(pathCandidate))
            {
                return pathCandidate;
            }
        }

        return null;
    }

    private static string? ResolveOnPath(string executableName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (var entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(entry, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
