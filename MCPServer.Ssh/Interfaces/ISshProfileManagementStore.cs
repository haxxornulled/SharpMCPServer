using LanguageExt;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Models;

namespace MCPServer.Ssh.Interfaces;

public interface ISshProfileManagementStore : ISshProfileStore
{
    ValueTask<Fin<SshProfileDefinition>> UpsertProfileAsync(
        string name,
        SshProfileUpsertRequest request,
        CancellationToken cancellationToken);

    ValueTask<Fin<SshProfileDefinition>> ReplaceProfileAsync(
        string name,
        SshProfileUpsertRequest request,
        CancellationToken cancellationToken);

    ValueTask<Fin<bool>> DeleteProfileAsync(
        string name,
        CancellationToken cancellationToken);

    ValueTask<Fin<SshProfileDefinition>> LinkPasswordAsync(
        string profileName,
        string credentialRef,
        CancellationToken cancellationToken);

    ValueTask<Fin<SshProfileDefinition>> LinkPrivateKeyPassphraseAsync(
        string profileName,
        string credentialRef,
        CancellationToken cancellationToken);

    ValueTask<Fin<IReadOnlyList<string>>> GetReferencedCredentialRefsAsync(CancellationToken cancellationToken);
}
