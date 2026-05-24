namespace MCPServer.Host.Sidecar.Vault;

public sealed record SshCredentialVaultEntry(
    string Name,
    string? Description,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record SshCredentialSecret
{
    public string Algorithm { get; init; } = "aesgcm-local-masterkey-v1";

    public string Nonce { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string Ciphertext { get; init; } = string.Empty;
}

internal sealed class SshVaultDocument
{
    public string Version { get; set; } = SshCredentialVaultStore.FileVersion;

    public Dictionary<string, SshVaultEntryDocument> Entries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SshVaultEntryDocument
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public SshCredentialSecret Secret { get; set; } = new();

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class SshVaultKeyDocument
{
    public string Version { get; set; } = SshCredentialVaultStore.FileVersion;

    public string MasterKey { get; set; } = string.Empty;
}
