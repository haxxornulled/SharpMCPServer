namespace MCPServer.Host.Sidecar.Profiles;

internal sealed class SshProfilesDocument
{
    public Dictionary<string, SidecarSshProfile> Profiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SidecarSshProfile
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public string? PrivateKeyPath { get; set; }

    public string? PrivateKeyPassphraseEnvironmentVariable { get; set; }

    public string? PasswordEnvironmentVariable { get; set; }

    public string? HostKeySha256 { get; set; }

    public bool AcceptUnknownHostKey { get; set; }

    public string? WorkingDirectory { get; set; }

    public string[] AllowedCommands { get; set; } = [];

    public string[] DeniedCommands { get; set; } = [];

    public string[] AllowedRemotePathPrefixes { get; set; } = [];

    public bool AllowSudoCommand { get; set; }

    public bool AllowAllCommands { get; set; }

    public bool Privileged { get; set; }

    public bool AllowedRoot { get; set; }
}
