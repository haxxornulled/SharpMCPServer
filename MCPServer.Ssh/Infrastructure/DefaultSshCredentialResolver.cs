using MCPServer.Ssh.Interfaces;
using Microsoft.Extensions.Logging;

namespace MCPServer.Ssh.Infrastructure;

public sealed class DefaultSshCredentialResolver : ISshCredentialResolver
{
    private readonly ISshCredentialVault _credentialVault;
    private readonly ILogger<DefaultSshCredentialResolver> _logger;

    public DefaultSshCredentialResolver(
        ISshCredentialVault credentialVault,
        ILogger<DefaultSshCredentialResolver> logger)
    {
        _credentialVault = credentialVault ?? throw new ArgumentNullException(nameof(credentialVault));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<string?> ResolveSecretAsync(
        string? credentialReference,
        CancellationToken cancellationToken)
    {
        var reference = TrimToNull(credentialReference);
        if (reference is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var sqliteSecret = await _credentialVault.ResolveSecretAsync(reference, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(sqliteSecret))
            {
                return sqliteSecret;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve SSH credential reference {CredentialReference} from SQLite credential vault.", reference);
        }
        return null;
    }

    public async ValueTask<bool> HasSecretAsync(
        string? credentialReference,
        CancellationToken cancellationToken)
    {
        var secret = await ResolveSecretAsync(credentialReference, cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrEmpty(secret);
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
