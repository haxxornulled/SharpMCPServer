namespace MCPServer.Ssh.Models;

public sealed record SshCredentialVaultEntry(
    string Name,
    string EnvironmentVariable,
    string? Description,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record SshCredentialSecret
{
    public string Algorithm { get; init; } = "aesgcm-sqlite-local-masterkey-v1";

    public string Nonce { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string Ciphertext { get; init; } = string.Empty;
}
