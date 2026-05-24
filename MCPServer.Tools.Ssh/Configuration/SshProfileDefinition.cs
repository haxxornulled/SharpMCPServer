namespace MCPServer.Tools.Ssh.Configuration;

public sealed class SshProfileDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 22;

    public string Username { get; init; } = string.Empty;

    public string? PrivateKeyPath { get; init; }

    public string? PrivateKeyPassphraseEnvironmentVariable { get; init; }

    public string? PasswordEnvironmentVariable { get; init; }

    public string? HostKeySha256 { get; init; }

    public bool AcceptUnknownHostKey { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyList<string> AllowedCommands { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DeniedCommands { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AllowedRemotePathPrefixes { get; init; } = Array.Empty<string>();

    public bool AllowSudoCommand { get; init; }

    public bool AllowAllCommands { get; init; }

    public bool Privileged { get; init; }

    public bool AllowedRoot { get; init; }

    public string Source { get; init; } = string.Empty;
}
