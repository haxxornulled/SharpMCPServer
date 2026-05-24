using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using MCPServer.Host.Sidecar.Json;

namespace MCPServer.Host.Sidecar.Vault;

public sealed class SshCredentialVaultStore
{
    internal const string FileVersion = "1";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _vaultPath;
    private readonly string _lockPath;
    private readonly SshCredentialProtector _protector;
    private readonly SemaphoreSlim _lock;

    public SshCredentialVaultStore(string vaultPath, string vaultKeyPath, string? baseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultKeyPath);

        var root = string.IsNullOrWhiteSpace(baseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(baseDirectory);

        _vaultPath = ResolvePath(vaultPath, root);
        _lockPath = _vaultPath + ".lock";
        _protector = new SshCredentialProtector(ResolvePath(vaultKeyPath, root));
        _lock = Locks.GetOrAdd(_vaultPath, static _ => new SemaphoreSlim(1, 1));
    }

    public async ValueTask<IReadOnlyList<SshCredentialVaultEntry>> ListEntriesAsync(CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            var count = document.Entries.Count;
            if (count == 0)
            {
                return Array.Empty<SshCredentialVaultEntry>();
            }

            var rented = ArrayPool<SshVaultEntryDocument>.Shared.Rent(count);
            try
            {
                var span = rented.AsSpan(0, count);
                var index = 0;
                foreach (var entry in document.Entries.Values)
                {
                    span[index++] = entry;
                }

                span.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

                var result = new SshCredentialVaultEntry[count];
                for (var resultIndex = 0; resultIndex < count; resultIndex++)
                {
                    var entry = span[resultIndex];
                    result[resultIndex] = new SshCredentialVaultEntry(entry.Name, entry.Description, entry.CreatedUtc, entry.UpdatedUtc);
                }

                return result;
            }
            finally
            {
                ArrayPool<SshVaultEntryDocument>.Shared.Return(rented, clearArray: true);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SshCredentialVaultEntry> UpsertEntryAsync(
        string name,
        string secret,
        string? description,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return await ExecuteAsync(async () =>
        {
            var key = NormalizeName(name);
            var now = DateTimeOffset.UtcNow;
            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            var existing = document.Entries.TryGetValue(key, out var current) ? current : null;
            var protectedSecret = await _protector.ProtectAsync(secret, cancellationToken).ConfigureAwait(false);
            var entry = new SshVaultEntryDocument
            {
                Name = key,
                Description = string.IsNullOrWhiteSpace(description) ? existing?.Description : description.Trim(),
                Secret = protectedSecret,
                CreatedUtc = existing?.CreatedUtc ?? now,
                UpdatedUtc = now
            };

            document.Entries[key] = entry;
            await SaveDocumentAsync(document, cancellationToken).ConfigureAwait(false);
            return new SshCredentialVaultEntry(entry.Name, entry.Description, entry.CreatedUtc, entry.UpdatedUtc);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> DeleteEntryAsync(string name, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var key = NormalizeName(name);
            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            if (!document.Entries.Remove(key))
            {
                return false;
            }

            await SaveDocumentAsync(document, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string> ResolveSecretAsync(string name, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var key = NormalizeName(name);
            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            if (!document.Entries.TryGetValue(key, out var entry))
            {
                throw new InvalidOperationException($"No SSH vault item named '{key}' was found.");
            }

            return await _protector.UnprotectAsync(entry.Secret, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> ExportReferencedEnvironmentAsync(
        IEnumerable<string> referencedEnvironmentVariables,
        CancellationToken cancellationToken)
    {
        var referenced = referencedEnvironmentVariables
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (referenced.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return await ExecuteAsync(async () =>
        {
            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in document.Entries.Values)
            {
                var environmentName = SshVaultEnvironment.BuildVariableName(entry.Name);
                if (!referenced.Contains(environmentName))
                {
                    continue;
                }

                result[environmentName] = await _protector.UnprotectAsync(entry.Secret, cancellationToken).ConfigureAwait(false);
            }

            return (IReadOnlyDictionary<string, string>)result;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<T> ExecuteAsync<T>(Func<ValueTask<T>> action, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_vaultPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var lockStream = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _ = lockStream;
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async ValueTask<SshVaultDocument> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_vaultPath))
        {
            return new SshVaultDocument();
        }

        await using var stream = File.OpenRead(_vaultPath);
        var document = await JsonSerializer.DeserializeAsync(
                stream,
                HostSidecarJsonSerializerContext.Default.SshVaultDocument,
                cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return new SshVaultDocument();
        }

        if (!string.Equals(document.Version, FileVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SSH vault file '{_vaultPath}' has unsupported version '{document.Version}'.");
        }

        document.Entries ??= new Dictionary<string, SshVaultEntryDocument>(StringComparer.OrdinalIgnoreCase);
        return document;
    }

    private async ValueTask SaveDocumentAsync(SshVaultDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_vaultPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? Directory.GetCurrentDirectory(), Path.GetRandomFileName());
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        document,
                        HostSidecarJsonSerializerContext.Default.SshVaultDocument,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, _vaultPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Vault item name is required.", nameof(name));
        }

        return name.Trim();
    }

    private static string ResolvePath(string path, string baseDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(baseDirectory, expanded));
    }
}

public static class SshVaultEnvironment
{
    private const string Prefix = "MCPSERVER_SSH_VAULT_";

    public static string BuildVariableName(string vaultItemName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultItemName);

        var buffer = new char[Prefix.Length + vaultItemName.Length];
        Prefix.AsSpan().CopyTo(buffer);
        var index = Prefix.Length;

        foreach (var ch in vaultItemName.Trim())
        {
            buffer[index++] = char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_';
        }

        return new string(buffer, 0, index);
    }
}
