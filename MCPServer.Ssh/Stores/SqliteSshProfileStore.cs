using LanguageExt;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Infrastructure;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace MCPServer.Ssh.Stores;

public sealed class SqliteSshProfileStore : ISshProfileManagementStore
{
    private const string DefaultDatabaseRelativePath = "ssh/ssh-store.db";
    private const string SchemaName = "ssh-profiles";
    private const string SourcePrefix = "sqlite:";

    private readonly IOptionsMonitor<SshToolSettings> _settings;
    private readonly ISshPathResolver _pathResolver;

    public SqliteSshProfileStore(
        IOptionsMonitor<SshToolSettings> settings,
        ISshPathResolver pathResolver)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    public async ValueTask<Fin<SshProfileCatalog>> LoadProfilesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var databasePath = ResolveDatabasePath();
            await using var connection = await OpenInitializedConnectionAsync(databasePath, cancellationToken).ConfigureAwait(false);
            var profiles = await LoadProfilesFromDatabaseAsync(connection, databasePath, cancellationToken).ConfigureAwait(false);

            return Fin.Succ(new SshProfileCatalog
            {
                Profiles = profiles,
                Sources = new List<SshProfileSourceStatus>
                {
                    new()
                    {
                        Path = databasePath,
                        Exists = File.Exists(databasePath),
                        ProfileCount = profiles.Count
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fin.Fail<SshProfileCatalog>(LanguageExt.Common.Error.New($"Failed to load SQLite SSH profiles: {ex.Message}"));
        }
    }

    public async ValueTask<Fin<SshProfileDefinition>> UpsertProfileAsync(
        string name,
        SshProfileUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return await SaveProfileAsync(name, request, replace: false, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Fin<SshProfileDefinition>> ReplaceProfileAsync(
        string name,
        SshProfileUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return await SaveProfileAsync(name, request, replace: true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Fin<bool>> DeleteProfileAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var key = NormalizeProfileName(name);
            var databasePath = ResolveDatabasePath();
            await using var connection = await OpenInitializedConnectionAsync(databasePath, cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var affected = await ExecuteNonQueryAsync(
                connection,
                transaction,
                "DELETE FROM ssh_profiles WHERE name = $name;",
                cancellationToken,
                new SqliteParameterValue("$name", key)).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Fin.Succ(affected > 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fin.Fail<bool>(LanguageExt.Common.Error.New($"Failed to delete SQLite SSH profile: {ex.Message}"));
        }
    }

    public async ValueTask<Fin<SshProfileDefinition>> LinkPasswordAsync(
        string profileName,
        string credentialRef,
        CancellationToken cancellationToken)
    {
        return await LinkCredentialAsync(
            profileName,
            request => new SshProfileUpsertRequest { PasswordEnvironmentVariable = credentialRef },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Fin<SshProfileDefinition>> LinkPrivateKeyPassphraseAsync(
        string profileName,
        string credentialRef,
        CancellationToken cancellationToken)
    {
        return await LinkCredentialAsync(
            profileName,
            request => new SshProfileUpsertRequest { PrivateKeyPassphraseEnvironmentVariable = credentialRef },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Fin<IReadOnlyList<string>>> GetReferencedCredentialRefsAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadProfilesAsync(cancellationToken).ConfigureAwait(false);
        return loaded.Match(
            Succ: catalog => Fin.Succ<IReadOnlyList<string>>(catalog.Profiles.Values
                .SelectMany(static profile => new[]
                {
                    profile.PasswordEnvironmentVariable,
                    profile.PrivateKeyPassphraseEnvironmentVariable
                })
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()),
            Fail: error => Fin.Fail<IReadOnlyList<string>>(error));
    }

    private async ValueTask<Fin<SshProfileDefinition>> LinkCredentialAsync(
        string profileName,
        Func<SshProfileDefinition, SshProfileUpsertRequest> requestFactory,
        CancellationToken cancellationToken)
    {
        var databasePath = ResolveDatabasePath();
        var key = NormalizeProfileName(profileName);
        await using var connection = await OpenInitializedConnectionAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var existing = await LoadProfileByNameAsync(connection, databasePath, key, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return Fin.Fail<SshProfileDefinition>(LanguageExt.Common.Error.New($"SSH profile '{key}' was not found."));
        }

        return await SaveProfileAsync(key, requestFactory(existing), existing, replace: false, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Fin<SshProfileDefinition>> SaveProfileAsync(
        string name,
        SshProfileUpsertRequest request,
        bool replace,
        CancellationToken cancellationToken)
    {
        return await SaveProfileAsync(name, request, existing: null, replace, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Fin<SshProfileDefinition>> SaveProfileAsync(
        string name,
        SshProfileUpsertRequest request,
        SshProfileDefinition? existing,
        bool replace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var key = NormalizeProfileName(name);
            var databasePath = ResolveDatabasePath();
            await using var connection = await OpenInitializedConnectionAsync(databasePath, cancellationToken).ConfigureAwait(false);

            var effectiveExisting = replace
                ? null
                : existing ?? await LoadProfileByNameAsync(connection, databasePath, key, cancellationToken).ConfigureAwait(false);
            var profile = BuildProfile(key, request, effectiveExisting, replace);

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await UpsertProfileCoreAsync(connection, transaction, profile, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Fin.Succ(profile);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fin.Fail<SshProfileDefinition>(LanguageExt.Common.Error.New($"Failed to save SQLite SSH profile: {ex.Message}"));
        }
    }

    private async ValueTask<SqliteConnection> OpenInitializedConnectionAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory);

        var connection = new SqliteConnection(BuildConnectionString(databasePath));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            await SqliteSshDatabaseInitializationCoordinator.EnsureInitializedAsync(
                databasePath,
                SchemaName,
                connection,
                InitializeSchemaAsync,
                cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private string ResolveDatabasePath()
    {
        var settings = SshToolSettings.Normalize(_settings.CurrentValue);
        return settings.ProfileDatabasePath is { Length: > 0 } configured
            ? _pathResolver.ResolveConfiguredPath(configured)
            : _pathResolver.ResolveUserDataPath(DefaultDatabaseRelativePath);
    }

    private static string BuildConnectionString(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
            DefaultTimeout = 30
        };

        return builder.ToString();
    }

    private static async ValueTask ConfigureConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA busy_timeout = 5000;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA synchronous = NORMAL;", cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask InitializeSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteScalarAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(connection, null, """
            CREATE TABLE IF NOT EXISTS ssh_profiles (
                name TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                display_name TEXT NOT NULL,
                host TEXT NOT NULL,
                port INTEGER NOT NULL,
                username TEXT NOT NULL,
                private_key_path TEXT NULL,
                private_key_passphrase_environment_variable TEXT NULL,
                password_environment_variable TEXT NULL,
                host_key_sha256 TEXT NULL,
                accept_unknown_host_key INTEGER NOT NULL,
                working_directory TEXT NULL,
                allow_sudo_command INTEGER NOT NULL,
                allow_all_commands INTEGER NOT NULL,
                privileged INTEGER NOT NULL,
                allowed_root INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """, cancellationToken).ConfigureAwait(false);

        await CreateStringTableAsync(connection, "ssh_profile_allowed_commands", cancellationToken).ConfigureAwait(false);
        await CreateStringTableAsync(connection, "ssh_profile_denied_commands", cancellationToken).ConfigureAwait(false);
        await CreateStringTableAsync(connection, "ssh_profile_allowed_remote_path_prefixes", cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask CreateStringTableAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, null, $"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                profile_name TEXT NOT NULL COLLATE NOCASE,
                value TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                PRIMARY KEY (profile_name, value),
                FOREIGN KEY (profile_name) REFERENCES ssh_profiles(name) ON DELETE CASCADE
            );
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<IReadOnlyDictionary<string, SshProfileDefinition>> LoadProfilesFromDatabaseAsync(
        SqliteConnection connection,
        string databasePath,
        CancellationToken cancellationToken)
    {
        var allowedCommands = await LoadStringValuesAsync(connection, "ssh_profile_allowed_commands", cancellationToken).ConfigureAwait(false);
        var deniedCommands = await LoadStringValuesAsync(connection, "ssh_profile_denied_commands", cancellationToken).ConfigureAwait(false);
        var allowedPathPrefixes = await LoadStringValuesAsync(connection, "ssh_profile_allowed_remote_path_prefixes", cancellationToken).ConfigureAwait(false);

        var profiles = new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                name,
                display_name,
                host,
                port,
                username,
                private_key_path,
                private_key_passphrase_environment_variable,
                password_environment_variable,
                host_key_sha256,
                accept_unknown_host_key,
                working_directory,
                allow_sudo_command,
                allow_all_commands,
                privileged,
                allowed_root
            FROM ssh_profiles
            ORDER BY name COLLATE NOCASE;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            profiles[name] = new SshProfileDefinition
            {
                Name = name,
                DisplayName = reader.GetString(1),
                Host = reader.GetString(2),
                Port = reader.GetInt32(3),
                Username = reader.GetString(4),
                PrivateKeyPath = ReadNullableString(reader, 5),
                PrivateKeyPassphraseEnvironmentVariable = ReadNullableString(reader, 6),
                PasswordEnvironmentVariable = ReadNullableString(reader, 7),
                HostKeySha256 = ReadNullableString(reader, 8),
                AcceptUnknownHostKey = ReadBoolean(reader, 9),
                WorkingDirectory = ReadNullableString(reader, 10),
                AllowSudoCommand = ReadBoolean(reader, 11),
                AllowAllCommands = ReadBoolean(reader, 12),
                Privileged = ReadBoolean(reader, 13),
                AllowedRoot = ReadBoolean(reader, 14),
                AllowedCommands = GetStringValues(allowedCommands, name),
                DeniedCommands = GetStringValues(deniedCommands, name),
                AllowedRemotePathPrefixes = GetStringValues(allowedPathPrefixes, name),
                Source = SourcePrefix + databasePath
            };
        }

        return profiles;
    }

    private static async ValueTask<SshProfileDefinition?> LoadProfileByNameAsync(
        SqliteConnection connection,
        string databasePath,
        string profileName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                name,
                display_name,
                host,
                port,
                username,
                private_key_path,
                private_key_passphrase_environment_variable,
                password_environment_variable,
                host_key_sha256,
                accept_unknown_host_key,
                working_directory,
                allow_sudo_command,
                allow_all_commands,
                privileged,
                allowed_root
            FROM ssh_profiles
            WHERE name = $name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$name", profileName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var allowedCommands = await LoadStringValuesForProfileAsync(connection, "ssh_profile_allowed_commands", profileName, cancellationToken).ConfigureAwait(false);
        var deniedCommands = await LoadStringValuesForProfileAsync(connection, "ssh_profile_denied_commands", profileName, cancellationToken).ConfigureAwait(false);
        var allowedPathPrefixes = await LoadStringValuesForProfileAsync(connection, "ssh_profile_allowed_remote_path_prefixes", profileName, cancellationToken).ConfigureAwait(false);

        return new SshProfileDefinition
        {
            Name = profileName,
            DisplayName = reader.GetString(1),
            Host = reader.GetString(2),
            Port = reader.GetInt32(3),
            Username = reader.GetString(4),
            PrivateKeyPath = ReadNullableString(reader, 5),
            PrivateKeyPassphraseEnvironmentVariable = ReadNullableString(reader, 6),
            PasswordEnvironmentVariable = ReadNullableString(reader, 7),
            HostKeySha256 = ReadNullableString(reader, 8),
            AcceptUnknownHostKey = ReadBoolean(reader, 9),
            WorkingDirectory = ReadNullableString(reader, 10),
            AllowSudoCommand = ReadBoolean(reader, 11),
            AllowAllCommands = ReadBoolean(reader, 12),
            Privileged = ReadBoolean(reader, 13),
            AllowedRoot = ReadBoolean(reader, 14),
            AllowedCommands = allowedCommands,
            DeniedCommands = deniedCommands,
            AllowedRemotePathPrefixes = allowedPathPrefixes,
            Source = SourcePrefix + databasePath
        };
    }

    private static async ValueTask<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadStringValuesAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT profile_name, value
            FROM {tableName}
            ORDER BY profile_name COLLATE NOCASE, ordinal;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var profileName = reader.GetString(0);
            var value = reader.GetString(1);
            if (!values.TryGetValue(profileName, out var profileValues))
            {
                profileValues = [];
                values[profileName] = profileValues;
            }

            profileValues.Add(value);
        }

        return values.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static async ValueTask<IReadOnlyList<string>> LoadStringValuesForProfileAsync(
        SqliteConnection connection,
        string tableName,
        string profileName,
        CancellationToken cancellationToken)
    {
        var values = new List<string>();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT value
            FROM {tableName}
            WHERE profile_name = $profileName COLLATE NOCASE
            ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$profileName", profileName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static async ValueTask UpsertProfileCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SshProfileDefinition profile,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var displayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Name : profile.DisplayName.Trim();

        await ExecuteNonQueryAsync(connection, transaction, """
            INSERT INTO ssh_profiles (
                name,
                display_name,
                host,
                port,
                username,
                private_key_path,
                private_key_passphrase_environment_variable,
                password_environment_variable,
                host_key_sha256,
                accept_unknown_host_key,
                working_directory,
                allow_sudo_command,
                allow_all_commands,
                privileged,
                allowed_root,
                created_at_utc,
                updated_at_utc)
            VALUES (
                $name,
                $displayName,
                $host,
                $port,
                $username,
                $privateKeyPath,
                $privateKeyPassphraseEnvironmentVariable,
                $passwordEnvironmentVariable,
                $hostKeySha256,
                $acceptUnknownHostKey,
                $workingDirectory,
                $allowSudoCommand,
                $allowAllCommands,
                $privileged,
                $allowedRoot,
                $now,
                $now)
            ON CONFLICT(name) DO UPDATE SET
                display_name = excluded.display_name,
                host = excluded.host,
                port = excluded.port,
                username = excluded.username,
                private_key_path = excluded.private_key_path,
                private_key_passphrase_environment_variable = excluded.private_key_passphrase_environment_variable,
                password_environment_variable = excluded.password_environment_variable,
                host_key_sha256 = excluded.host_key_sha256,
                accept_unknown_host_key = excluded.accept_unknown_host_key,
                working_directory = excluded.working_directory,
                allow_sudo_command = excluded.allow_sudo_command,
                allow_all_commands = excluded.allow_all_commands,
                privileged = excluded.privileged,
                allowed_root = excluded.allowed_root,
                updated_at_utc = excluded.updated_at_utc;
            """, cancellationToken,
            new SqliteParameterValue("$name", profile.Name.Trim()),
            new SqliteParameterValue("$displayName", displayName),
            new SqliteParameterValue("$host", profile.Host.Trim()),
            new SqliteParameterValue("$port", profile.Port <= 0 ? 22 : profile.Port),
            new SqliteParameterValue("$username", profile.Username.Trim()),
            new SqliteParameterValue("$privateKeyPath", NullIfWhiteSpace(profile.PrivateKeyPath)),
            new SqliteParameterValue("$privateKeyPassphraseEnvironmentVariable", NullIfWhiteSpace(profile.PrivateKeyPassphraseEnvironmentVariable)),
            new SqliteParameterValue("$passwordEnvironmentVariable", NullIfWhiteSpace(profile.PasswordEnvironmentVariable)),
            new SqliteParameterValue("$hostKeySha256", NullIfWhiteSpace(profile.HostKeySha256)),
            new SqliteParameterValue("$acceptUnknownHostKey", profile.AcceptUnknownHostKey ? 1 : 0),
            new SqliteParameterValue("$workingDirectory", NullIfWhiteSpace(profile.WorkingDirectory)),
            new SqliteParameterValue("$allowSudoCommand", profile.AllowSudoCommand ? 1 : 0),
            new SqliteParameterValue("$allowAllCommands", profile.AllowAllCommands ? 1 : 0),
            new SqliteParameterValue("$privileged", profile.Privileged ? 1 : 0),
            new SqliteParameterValue("$allowedRoot", profile.AllowedRoot ? 1 : 0),
            new SqliteParameterValue("$now", now)).ConfigureAwait(false);

        await ReplaceStringValuesAsync(connection, transaction, "ssh_profile_allowed_commands", profile.Name, profile.AllowedCommands, cancellationToken).ConfigureAwait(false);
        await ReplaceStringValuesAsync(connection, transaction, "ssh_profile_denied_commands", profile.Name, profile.DeniedCommands, cancellationToken).ConfigureAwait(false);
        await ReplaceStringValuesAsync(connection, transaction, "ssh_profile_allowed_remote_path_prefixes", profile.Name, profile.AllowedRemotePathPrefixes, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ReplaceStringValuesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string profileName,
        IEnumerable<string> values,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, transaction, $"DELETE FROM {tableName} WHERE profile_name = $profileName;", cancellationToken, new SqliteParameterValue("$profileName", profileName)).ConfigureAwait(false);

        var ordinal = 0;
        foreach (var value in NormalizeStrings(values))
        {
            await ExecuteNonQueryAsync(connection, transaction, $"""
                INSERT INTO {tableName} (profile_name, value, ordinal)
                VALUES ($profileName, $value, $ordinal);
                """, cancellationToken,
                new SqliteParameterValue("$profileName", profileName),
                new SqliteParameterValue("$value", value),
                new SqliteParameterValue("$ordinal", ordinal++)).ConfigureAwait(false);
        }
    }

    private static SshProfileDefinition BuildProfile(
        string name,
        SshProfileUpsertRequest request,
        SshProfileDefinition? existing,
        bool replace)
    {
        var fallback = replace ? null : existing;
        var displayName = request.DisplayName ?? fallback?.DisplayName ?? name;

        return new SshProfileDefinition
        {
            Name = name,
            DisplayName = displayName,
            Host = request.Host ?? fallback?.Host ?? string.Empty,
            Port = request.Port ?? fallback?.Port ?? 22,
            Username = request.Username ?? fallback?.Username ?? string.Empty,
            PrivateKeyPath = request.PrivateKeyPath ?? fallback?.PrivateKeyPath,
            PrivateKeyPassphraseEnvironmentVariable = request.PrivateKeyPassphraseEnvironmentVariable ?? fallback?.PrivateKeyPassphraseEnvironmentVariable,
            PasswordEnvironmentVariable = request.PasswordEnvironmentVariable ?? fallback?.PasswordEnvironmentVariable,
            HostKeySha256 = request.HostKeySha256 ?? fallback?.HostKeySha256,
            AcceptUnknownHostKey = request.AcceptUnknownHostKey ?? fallback?.AcceptUnknownHostKey ?? false,
            WorkingDirectory = request.WorkingDirectory ?? fallback?.WorkingDirectory,
            AllowedCommands = request.AllowedCommands.Count == 0 ? fallback?.AllowedCommands ?? Array.Empty<string>() : NormalizeStrings(request.AllowedCommands),
            DeniedCommands = request.DeniedCommands.Count == 0 ? fallback?.DeniedCommands ?? Array.Empty<string>() : NormalizeStrings(request.DeniedCommands),
            AllowedRemotePathPrefixes = request.AllowedRemotePathPrefixes.Count == 0 ? fallback?.AllowedRemotePathPrefixes ?? Array.Empty<string>() : NormalizeStrings(request.AllowedRemotePathPrefixes),
            AllowSudoCommand = request.AllowSudoCommand ?? fallback?.AllowSudoCommand ?? false,
            AllowAllCommands = request.AllowAllCommands ?? fallback?.AllowAllCommands ?? false,
            Privileged = request.Privileged ?? fallback?.Privileged ?? false,
            AllowedRoot = request.AllowedRoot ?? fallback?.AllowedRoot ?? false,
            Source = string.Empty
        };
    }

    private static async ValueTask<int> ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken,
        params SqliteParameterValue[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = transaction;

        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<object?> ExecuteScalarAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> GetStringValues(
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        string profileName)
    {
        return values.TryGetValue(profileName, out var profileValues)
            ? profileValues.ToArray()
            : Array.Empty<string>();
    }

    private static string NormalizeProfileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("SSH profile name is required.", nameof(name));
        }

        return name.Trim();
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static bool ReadBoolean(SqliteDataReader reader, int ordinal)
    {
        return reader.GetInt64(ordinal) != 0;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string[] NormalizeStrings(IEnumerable<string>? values)
    {
        return values is null
            ? []
            : values
                .Select(NullIfWhiteSpace)
                .Where(static value => value is not null)
                .Select(static value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private readonly record struct SqliteParameterValue(string Name, object? Value);
}
