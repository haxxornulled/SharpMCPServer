using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MCPServer.Host.Sidecar.Json;

namespace MCPServer.Host.Sidecar.Vault;

internal sealed class SshCredentialProtector
{
    private const string AlgorithmName = "aesgcm-local-masterkey-v1";
    private const int MasterKeyLengthBytes = 32;
    private const int NonceLengthBytes = 12;
    private const int TagLengthBytes = 16;

    private readonly string _vaultKeyPath;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private byte[]? _masterKey;

    public SshCredentialProtector(string vaultKeyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultKeyPath);
        _vaultKeyPath = Path.GetFullPath(vaultKeyPath);
    }

    public async ValueTask<SshCredentialSecret> ProtectAsync(string secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();

        var masterKey = await LoadOrCreateMasterKeyAsync(cancellationToken).ConfigureAwait(false);
        var nonce = RandomNumberGenerator.GetBytes(NonceLengthBytes);
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLengthBytes];

        try
        {
            using var aes = new AesGcm(masterKey, TagLengthBytes);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            return new SshCredentialSecret
            {
                Algorithm = AlgorithmName,
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                Ciphertext = Convert.ToBase64String(ciphertext)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    public async ValueTask<string> UnprotectAsync(SshCredentialSecret secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(secret.Algorithm, AlgorithmName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported SSH credential algorithm '{secret.Algorithm}'. Expected '{AlgorithmName}'.");
        }

        var masterKey = await LoadOrCreateMasterKeyAsync(cancellationToken).ConfigureAwait(false);
        var nonce = Convert.FromBase64String(secret.Nonce);
        var tag = Convert.FromBase64String(secret.Tag);
        var ciphertext = Convert.FromBase64String(secret.Ciphertext);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(masterKey, TagLengthBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    private async ValueTask<byte[]> LoadOrCreateMasterKeyAsync(CancellationToken cancellationToken)
    {
        await _keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_masterKey is not null)
            {
                return (byte[])_masterKey.Clone();
            }

            var directory = Path.GetDirectoryName(_vaultKeyPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_vaultKeyPath))
            {
                await using var readStream = File.OpenRead(_vaultKeyPath);
                var document = await JsonSerializer.DeserializeAsync(
                        readStream,
                        HostSidecarJsonSerializerContext.Default.SshVaultKeyDocument,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (document is null)
                {
                    throw new InvalidOperationException($"SSH vault key file '{_vaultKeyPath}' is empty or invalid.");
                }

                if (!string.Equals(document.Version, SshCredentialVaultStore.FileVersion, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"SSH vault key file '{_vaultKeyPath}' has unsupported version '{document.Version}'.");
                }

                _masterKey = Convert.FromBase64String(document.MasterKey);
                return (byte[])_masterKey.Clone();
            }

            _masterKey = RandomNumberGenerator.GetBytes(MasterKeyLengthBytes);
            var payload = new SshVaultKeyDocument
            {
                Version = SshCredentialVaultStore.FileVersion,
                MasterKey = Convert.ToBase64String(_masterKey)
            };

            await using var writeStream = File.Create(_vaultKeyPath);
            await JsonSerializer.SerializeAsync(
                    writeStream,
                    payload,
                    HostSidecarJsonSerializerContext.Default.SshVaultKeyDocument,
                    cancellationToken)
                .ConfigureAwait(false);

            return (byte[])_masterKey.Clone();
        }
        finally
        {
            _keyLock.Release();
        }
    }
}
