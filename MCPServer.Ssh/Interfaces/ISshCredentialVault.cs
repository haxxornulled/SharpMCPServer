using LanguageExt;
using MCPServer.Ssh.Models;

namespace MCPServer.Ssh.Interfaces;

public interface ISshCredentialVault
{
    ValueTask<IReadOnlyList<SshCredentialVaultEntry>> ListEntriesAsync(CancellationToken cancellationToken);

    ValueTask<Fin<SshCredentialVaultEntry>> UpsertEntryAsync(
        string name,
        string secret,
        string? description,
        CancellationToken cancellationToken);

    ValueTask<bool> DeleteEntryAsync(string name, CancellationToken cancellationToken);

    ValueTask<string?> ResolveSecretAsync(string credentialReference, CancellationToken cancellationToken);

    ValueTask<bool> HasSecretAsync(string credentialReference, CancellationToken cancellationToken);
}
