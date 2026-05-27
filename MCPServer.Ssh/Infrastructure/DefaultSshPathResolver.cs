using MCPServer.Ssh.Interfaces;

namespace MCPServer.Ssh.Infrastructure;

public sealed class DefaultSshPathResolver : ISshPathResolver
{
    private const string AppDirectoryName = "McpServer";

    private readonly string _contentRoot;
    private readonly string _userRoot;
    private readonly string _legacyRoamingUserRoot;

    public DefaultSshPathResolver()
    {
        _contentRoot = AppContext.BaseDirectory;
        _userRoot = BuildUserRoot(Environment.SpecialFolder.LocalApplicationData);
        _legacyRoamingUserRoot = BuildUserRoot(Environment.SpecialFolder.ApplicationData);
    }

    public string ResolveConfiguredPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var trimmed = path.Trim();
        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : ResolveContentPath(trimmed);
    }

    public string ResolveContentPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return Path.GetFullPath(Path.Combine(_contentRoot, relativePath));
    }

    public string ResolveUserDataPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return Path.GetFullPath(Path.Combine(_userRoot, relativePath));
    }

    public string ResolveLegacyRoamingUserDataPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return Path.GetFullPath(Path.Combine(_legacyRoamingUserRoot, relativePath));
    }

    private static string BuildUserRoot(Environment.SpecialFolder folder)
    {
        var appData = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mcpserver")
            : Path.Combine(appData, AppDirectoryName);
    }
}
