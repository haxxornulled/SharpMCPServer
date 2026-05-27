namespace MCPServer.Ssh.Interfaces;

public interface ISshCredentialResolver
{
    ValueTask<string?> ResolveSecretAsync(
        string? credentialReference,
        CancellationToken cancellationToken);

    ValueTask<bool> HasSecretAsync(
        string? credentialReference,
        CancellationToken cancellationToken);
}
