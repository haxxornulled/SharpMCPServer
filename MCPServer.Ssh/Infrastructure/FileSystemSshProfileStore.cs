using System.Text.Json;
using LanguageExt;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MCPServer.Ssh.Infrastructure;

public sealed class FileSystemSshProfileStore : ISshProfileStore
{
    private readonly IOptionsMonitor<SshToolSettings> _settings;
    private readonly ISshPathResolver _pathResolver;
    private readonly ILogger<FileSystemSshProfileStore> _logger;

    public FileSystemSshProfileStore(
        IOptionsMonitor<SshToolSettings> settings,
        ISshPathResolver pathResolver,
        ILogger<FileSystemSshProfileStore> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Fin<SshProfileCatalog>> LoadProfilesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var profiles = new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<SshProfileSourceStatus>();

            foreach (var path in ResolveCandidatePaths())
            {
                var loaded = await TryLoadProfileFileAsync(path, cancellationToken).ConfigureAwait(false);
                sources.Add(loaded.Status);

                foreach (var profile in loaded.Profiles)
                {
                    profiles[profile.Name] = profile;
                }
            }

            return Fin.Succ<SshProfileCatalog>(new SshProfileCatalog
            {
                Profiles = profiles,
                Sources = sources
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fin.Fail<SshProfileCatalog>(LanguageExt.Common.Error.New($"Failed to load SSH profiles: {ex.Message}"));
        }
    }

    private async ValueTask<ProfileFileLoadResult> TryLoadProfileFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new ProfileFileLoadResult(
                Array.Empty<SshProfileDefinition>(),
                new SshProfileSourceStatus
                {
                    Path = path,
                    Exists = false,
                    ProfileCount = 0
                });
        }

        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var result = ReadProfiles(document.RootElement, path);

            return new ProfileFileLoadResult(
                result,
                new SshProfileSourceStatus
                {
                    Path = path,
                    Exists = true,
                    ProfileCount = result.Length
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load SSH profile file {Path}", path);
            return new ProfileFileLoadResult(
                Array.Empty<SshProfileDefinition>(),
                new SshProfileSourceStatus
                {
                    Path = path,
                    Exists = true,
                    ProfileCount = 0,
                    Error = ex.Message
                });
        }
    }

    private SshProfileDefinition[] ReadProfiles(JsonElement root, string source)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("profiles"u8, out var profilesElement))
        {
            return Array.Empty<SshProfileDefinition>();
        }

        var list = new List<SshProfileDefinition>();

        if (profilesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in profilesElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    list.Add(ReadProfile(property.Name, property.Value, source));
                }
            }
        }
        else if (profilesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in profilesElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = ReadString(item, "name") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(ReadProfile(name, item, source));
                }
            }
        }

        return list
            .Where(static profile => !string.IsNullOrWhiteSpace(profile.Name))
            .ToArray();
    }

    private static SshProfileDefinition ReadProfile(string name, JsonElement element, string source)
    {
        return new SshProfileDefinition
        {
            Name = name.Trim(),
            DisplayName = ReadString(element, "displayName") ?? ReadString(element, "friendlyName") ?? name.Trim(),
            Host = ReadString(element, "host") ?? string.Empty,
            Port = Math.Clamp(ReadInt(element, "port", 22), 1, 65_535),
            Username = ReadString(element, "username") ?? string.Empty,
            PrivateKeyPath = ReadString(element, "privateKeyPath"),
            PrivateKeyPassphraseEnvironmentVariable = ReadString(element, "privateKeyPassphraseEnvironmentVariable"),
            PasswordEnvironmentVariable = ReadString(element, "passwordEnvironmentVariable"),
            HostKeySha256 = ReadString(element, "hostKeySha256"),
            AcceptUnknownHostKey = ReadBool(element, "acceptUnknownHostKey", false),
            WorkingDirectory = ReadString(element, "workingDirectory"),
            AllowedCommands = ReadStringArray(element, "allowedCommands"),
            DeniedCommands = ReadStringArray(element, "deniedCommands"),
            AllowedRemotePathPrefixes = ReadStringArray(element, "allowedRemotePathPrefixes"),
            AllowSudoCommand = ReadBool(element, "allowSudoCommand", false),
            AllowAllCommands = ReadBool(element, "allowAllCommands", false),
            Privileged = ReadBool(element, "privileged", false),
            AllowedRoot = ReadBool(element, "allowedRoot", false),
            Source = source
        };
    }

    private IEnumerable<string> ResolveCandidatePaths()
    {
        var settings = SshToolSettings.Normalize(_settings.CurrentValue);
        if (!string.IsNullOrWhiteSpace(settings.ProfilePath))
        {
            yield return _pathResolver.ResolveConfiguredPath(settings.ProfilePath);
        }

        yield return _pathResolver.ResolveContentPath(Path.Combine("config", "mcpserver", "ssh-profiles.local.json"));
        yield return _pathResolver.ResolveUserDataPath(Path.Combine("ssh", "ssh-profiles.local.json"));

        // Temporary compatibility path for builds that used Roaming AppData before the
        // sidecar/server default path was unified on LocalAppData. Keep this as a
        // fallback only; the sidecar writes the canonical LocalAppData file.
        yield return _pathResolver.ResolveLegacyRoamingUserDataPath(Path.Combine("ssh", "ssh-profiles.local.json"));
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? TrimToNull(property.GetString())
            : null;
    }

    private static int ReadInt(JsonElement element, string propertyName, int defaultValue)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : defaultValue;
    }

    private static bool ReadBool(JsonElement element, string propertyName, bool defaultValue)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : defaultValue;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => TrimToNull(item.GetString()))
            .Where(static value => value is not null)
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record ProfileFileLoadResult(
        IReadOnlyList<SshProfileDefinition> Profiles,
        SshProfileSourceStatus Status);
}
