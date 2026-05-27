namespace MCPServer.Ssh.Models;

public sealed class SshProfileUpsertRequest
{
    public string? DisplayName { get; init; }

    public string? Host { get; init; }

    public int? Port { get; init; }

    public string? Username { get; init; }

    public string? PrivateKeyPath { get; init; }

    public string? PrivateKeyPassphraseCredentialReference { get; init; }

    public string? PasswordCredentialReference { get; init; }

    public string? HostKeySha256 { get; init; }

    public bool? AcceptUnknownHostKey { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyList<string> AllowedCommands { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DeniedCommands { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AllowedRemotePathPrefixes { get; init; } = Array.Empty<string>();

    public bool? AllowSudoCommand { get; init; }

    public bool? AllowAllCommands { get; init; }

    public bool? Privileged { get; init; }

    public bool? AllowedRoot { get; init; }
}
