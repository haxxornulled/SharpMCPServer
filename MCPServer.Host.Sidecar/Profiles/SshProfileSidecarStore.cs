using System.Text.Json;
using MCPServer.Host.Sidecar.Json;
using MCPServer.Host.Sidecar.Vault;

namespace MCPServer.Host.Sidecar.Profiles;

internal sealed class SshProfileSidecarStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SshProfileSidecarStore(string path, string? baseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var root = string.IsNullOrWhiteSpace(baseDirectory)
            ? Directory.GetCurrentDirectory()
            : System.IO.Path.GetFullPath(baseDirectory);
        _path = ResolvePath(path, root);
    }

    public string Path => _path;

    public async ValueTask<IReadOnlyDictionary<string, SidecarSshProfile>> ListProfilesAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, SidecarSshProfile>(document.Profiles, StringComparer.OrdinalIgnoreCase);
    }

    public async ValueTask<SidecarSshProfile> UpsertProfileAsync(
        string name,
        ProfileUpsertRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(request);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var key = name.Trim();
            var existing = document.Profiles.TryGetValue(key, out var current) ? current : new SidecarSshProfile();
            var profile = new SidecarSshProfile
            {
                Host = request.Host ?? existing.Host,
                Port = request.Port ?? existing.Port,
                Username = request.Username ?? existing.Username,
                PrivateKeyPath = request.PrivateKeyPath ?? existing.PrivateKeyPath,
                PrivateKeyPassphraseEnvironmentVariable = request.PrivateKeyPassphraseVaultItem is null
                    ? existing.PrivateKeyPassphraseEnvironmentVariable
                    : SshVaultEnvironment.BuildVariableName(request.PrivateKeyPassphraseVaultItem),
                PasswordEnvironmentVariable = request.PasswordVaultItem is null
                    ? existing.PasswordEnvironmentVariable
                    : SshVaultEnvironment.BuildVariableName(request.PasswordVaultItem),
                HostKeySha256 = request.HostKeySha256 ?? existing.HostKeySha256,
                AcceptUnknownHostKey = request.AcceptUnknownHostKey ?? existing.AcceptUnknownHostKey,
                WorkingDirectory = request.WorkingDirectory ?? existing.WorkingDirectory,
                AllowedCommands = request.AllowedCommands.Count == 0 ? existing.AllowedCommands : request.AllowedCommands.ToArray(),
                DeniedCommands = request.DeniedCommands.Count == 0 ? existing.DeniedCommands : request.DeniedCommands.ToArray(),
                AllowedRemotePathPrefixes = request.AllowedRemotePathPrefixes.Count == 0 ? existing.AllowedRemotePathPrefixes : request.AllowedRemotePathPrefixes.ToArray(),
                AllowSudoCommand = request.AllowSudoCommand ?? existing.AllowSudoCommand,
                AllowAllCommands = request.AllowAllCommands ?? existing.AllowAllCommands,
                Privileged = request.Privileged ?? existing.Privileged,
                AllowedRoot = request.AllowedRoot ?? existing.AllowedRoot
            };

            document.Profiles[key] = profile;
            await SaveCoreAsync(document, cancellationToken).ConfigureAwait(false);
            return profile;
        }
        finally
        {
            _lock.Release();
        }
    }


    public async ValueTask<SidecarSshProfile> ReplaceProfileAsync(
        string name,
        ProfileUpsertRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(request);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var key = name.Trim();
            var profile = new SidecarSshProfile
            {
                Host = request.Host ?? string.Empty,
                Port = request.Port ?? 22,
                Username = request.Username ?? string.Empty,
                PrivateKeyPath = request.PrivateKeyPath,
                PrivateKeyPassphraseEnvironmentVariable = request.PrivateKeyPassphraseVaultItem is null
                    ? null
                    : SshVaultEnvironment.BuildVariableName(request.PrivateKeyPassphraseVaultItem),
                PasswordEnvironmentVariable = request.PasswordVaultItem is null
                    ? null
                    : SshVaultEnvironment.BuildVariableName(request.PasswordVaultItem),
                HostKeySha256 = request.HostKeySha256,
                AcceptUnknownHostKey = request.AcceptUnknownHostKey ?? false,
                WorkingDirectory = request.WorkingDirectory,
                AllowedCommands = request.AllowedCommands.ToArray(),
                DeniedCommands = request.DeniedCommands.ToArray(),
                AllowedRemotePathPrefixes = request.AllowedRemotePathPrefixes.ToArray(),
                AllowSudoCommand = request.AllowSudoCommand ?? false,
                AllowAllCommands = request.AllowAllCommands ?? false,
                Privileged = request.Privileged ?? false,
                AllowedRoot = request.AllowedRoot ?? false
            };

            document.Profiles[key] = profile;
            await SaveCoreAsync(document, cancellationToken).ConfigureAwait(false);
            return profile;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask<bool> DeleteProfileAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!document.Profiles.Remove(name.Trim()))
            {
                return false;
            }

            await SaveCoreAsync(document, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask<SidecarSshProfile> LinkPasswordAsync(string profileName, string vaultItemName, CancellationToken cancellationToken)
    {
        return await LinkAsync(
            profileName,
            profile => profile.PasswordEnvironmentVariable = SshVaultEnvironment.BuildVariableName(vaultItemName),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SidecarSshProfile> LinkPrivateKeyPassphraseAsync(string profileName, string vaultItemName, CancellationToken cancellationToken)
    {
        return await LinkAsync(
            profileName,
            profile => profile.PrivateKeyPassphraseEnvironmentVariable = SshVaultEnvironment.BuildVariableName(vaultItemName),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<string>> GetReferencedEnvironmentVariablesAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.Profiles.Values
            .SelectMany(static profile => new[]
            {
                profile.PasswordEnvironmentVariable,
                profile.PrivateKeyPassphraseEnvironmentVariable
            })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async ValueTask<SidecarSshProfile> LinkAsync(
        string profileName,
        Action<SidecarSshProfile> update,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentNullException.ThrowIfNull(update);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var key = profileName.Trim();
            if (!document.Profiles.TryGetValue(key, out var profile))
            {
                throw new InvalidOperationException($"SSH profile '{key}' was not found.");
            }

            update(profile);
            await SaveCoreAsync(document, cancellationToken).ConfigureAwait(false);
            return profile;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async ValueTask<SshProfilesDocument> LoadAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async ValueTask<SshProfilesDocument> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new SshProfilesDocument();
        }

        await using var stream = File.OpenRead(_path);
        var document = await JsonSerializer.DeserializeAsync(
                stream,
                HostSidecarJsonSerializerContext.Default.SshProfilesDocument,
                cancellationToken)
            .ConfigureAwait(false);

        document ??= new SshProfilesDocument();
        document.Profiles ??= new Dictionary<string, SidecarSshProfile>(StringComparer.OrdinalIgnoreCase);
        return document;
    }

    private async ValueTask SaveCoreAsync(SshProfilesDocument document, CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = System.IO.Path.Combine(directory ?? Directory.GetCurrentDirectory(), System.IO.Path.GetRandomFileName());
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        document,
                        HostSidecarJsonSerializerContext.Default.SshProfilesDocument,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, _path, overwrite: true);
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

    private static string ResolvePath(string path, string baseDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return System.IO.Path.IsPathRooted(expanded)
            ? System.IO.Path.GetFullPath(expanded)
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, expanded));
    }
}

internal sealed class ProfileUpsertRequest
{
    public string? Host { get; init; }

    public int? Port { get; init; }

    public string? Username { get; init; }

    public string? PrivateKeyPath { get; init; }

    public string? PasswordVaultItem { get; init; }

    public string? PrivateKeyPassphraseVaultItem { get; init; }

    public string? HostKeySha256 { get; init; }

    public bool? AcceptUnknownHostKey { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyList<string> AllowedCommands { get; init; } = [];

    public IReadOnlyList<string> DeniedCommands { get; init; } = [];

    public IReadOnlyList<string> AllowedRemotePathPrefixes { get; init; } = [];

    public bool? AllowSudoCommand { get; init; }

    public bool? AllowAllCommands { get; init; }

    public bool? Privileged { get; init; }

    public bool? AllowedRoot { get; init; }
}
