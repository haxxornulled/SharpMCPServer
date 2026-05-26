namespace MCPServer.Ssh.Interfaces;

public interface ISshCredentialResolver
{
    ValueTask<string?> ResolveSecretAsync(
        string? environmentVariableName,
        CancellationToken cancellationToken);

    ValueTask<bool> HasSecretAsync(
        string? environmentVariableName,
        CancellationToken cancellationToken);
}
