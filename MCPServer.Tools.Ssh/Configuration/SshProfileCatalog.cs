namespace MCPServer.Tools.Ssh.Configuration;

public sealed class SshProfileCatalog
{
    public IReadOnlyDictionary<string, SshProfileDefinition> Profiles { get; init; } =
        new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SshProfileSourceStatus> Sources { get; init; } = Array.Empty<SshProfileSourceStatus>();
}

public sealed class SshProfileSourceStatus
{
    public string Path { get; init; } = string.Empty;

    public bool Exists { get; init; }

    public int ProfileCount { get; init; }

    public string? Error { get; init; }
}
