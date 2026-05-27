using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LanguageExt;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace MCPServer.Ssh.Stores;

public sealed class SqliteSshCredentialVault : ISshCredentialVault
{
    private const string DefaultDatabaseRelativePath = "ssh/ssh-store.db";
    private const string SchemaName = "ssh-credentials";
    private const string MasterKeyName = "default";
    private const string SchemaVersion = "1";
    private const string AlgorithmName = "aesgcm-sqlite-local-masterkey-v1";
    private const int MasterKeyLengthBytes = 32;
    private const int NonceLengthBytes = 12;
    private const int TagLengthBytes = 16;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> MasterKeyLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly IOptionsMonitor<SshToolSettings> _settings;
    private readonly ISshPathResolver _pathResolver;

    public SqliteSshCredentialVault(
        IOptionsMonitor<SshToolSettings> settings,
        ISshPathResolver pathResolver)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    public async ValueTask<IReadOnlyList<SshCredentialVaultEntry>> ListEntriesAsync(CancellationToken cancellationToken)
    {
        var databasePath = ResolveDatabasePath();
        await using var connection = await OpenInitializedConnectionAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name, credential_reference, description, created_at_utc, updated_at_utc
            FROM ssh_credentials
            ORDER BY name COLLATE NOCASE;
            """;

        var entries = new List<SshCredentialVaultEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new SshCredentialVaultEntry(
                reader.GetString(0),
                reader.GetString(1),
                ReadNullableString(reader, 2),
                DateTimeOffset.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                DateTimeOffset.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }

        return entries.ToArray();
    }

    public async ValueTask<Fin<SshCredentialVaultEntry>> UpsertEntryAsync(
        string name,
        string secret,
        string? description,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(secret);

        try
        {
            var key = NormalizeName(name);
            var databasePath = ResolveDatabasePath();

            for (var attempt = 0; attempt < 3; attempt++)
            {
                await using var connection = await OpenInitializedConnectionAsync(databasePath, cancellationToken).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                var now = DateTimeOffset.UtcNow;
                var existing = await LoadEntryForUpdateAsync(connection, transaction, key, cancellationToken).ConfigureAwait(false);
                var credentialReference = key;
                var protectedSecret = await ProtectAsync(connection, transaction, databasePath, secret, cancellationToken).ConfigureAwait(false);
                var createdUtc = existing?.CreatedUtc ?? now;
                var effectiveDescription = string.IsNullOrWhiteSpace(description) ? existing?.Description : description.Trim();
                var secretRow = new SshCredentialSecret
                {
                    Algorithm = protectedSecret.Algorithm,
                    Nonce = protectedSecret.Nonce,
                    Tag = protectedSecret.Tag,
                    Ciphertext = protectedSecret.Ciphertext
                };

                if (existing is null)
                {
                    try
                    {
                        await InsertEntryAsync(
                            connection,
                            transaction,
                            key,
                            credentialReference,
                            effectiveDescription,
                            secretRow,
                            createdUtc,
                            now,
                            cancellationToken).ConfigureAwait(false);

                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        return Fin.Succ(new SshCredentialVaultEntry(key, credentialReference, effectiveDescription, createdUtc, now));
                    }
                    catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
                    {
                        continue;
                    }
                }
                else
                {
                    var updated = await UpdateEntryAsync(
                        connection,
                        transaction,
                        key,
                        credentialReference,
                        effectiveDescription,
                        secretRow,
                        now,
                        cancellationToken).ConfigureAwait(false);

                    if (updated > 0)
                    {
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        return Fin.Succ(new SshCredentialVaultEntry(key, credentialReference, effectiveDescription, createdUtc, now));
                    }
                }
            }

            return Fin.Fail<SshCredentialVaultEntry>(LanguageExt.Common.Error.New($"Failed to save SSH credential '{NormalizeName(name)}' because the row changed concurrently. Please try again."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fin.Fail<SshCredentialVaultEntry>(LanguageExt.Common.Error.New($"Failed to save SQLite SSH credential: {ex.Message}"));
        }
    }

    public async ValueTask<bool> DeleteEntryAsync(string name, CancellationToken cancellationToken)
    {
        var databasePath = ResolveDatabasePath();
        await using var connection = await OpenInitializedConnectionAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var affected = await ExecuteNonQueryAsync(connection, transaction,
            "DELETE FROM ssh_credentials WHERE name = $name;",
            cancellationToken,
            new SqliteParameterValue("$name", NormalizeName(name))).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    public async ValueTask<string?> ResolveSecretAsync(string credentialReference, CancellationToken cancellationToken)
    {
        var reference = TrimToNull(credentialReference);
        if (reference is null)
        {
            return null;
        }

        var databasePath = ResolveDatabasePath();
        await using var connection = await OpenInitializedConnectionAsync(databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var row = await LoadSecretByReferenceAsync(connection, transaction, reference, cancellationToken).ConfigureAwait(false);
        if (row is not { } secretRow)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var secret = await UnprotectAsync(connection, transaction, databasePath, secretRow, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return secret;
    }

    public async ValueTask<bool> HasSecretAsync(string credentialReference, CancellationToken cancellationToken)
    {
        var secret = await ResolveSecretAsync(credentialReference, cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrEmpty(secret);
    }

    private async ValueTask<SqliteConnection> OpenInitializedConnectionAsync(string databasePath, CancellationToken cancellationToken)
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
            CREATE TABLE IF NOT EXISTS ssh_vault_master_keys (
                name TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                version TEXT NOT NULL,
                algorithm TEXT NOT NULL,
                master_key TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """, cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(connection, null, """
            CREATE TABLE IF NOT EXISTS ssh_credentials (
                name TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                credential_reference TEXT NOT NULL UNIQUE COLLATE NOCASE,
                description TEXT NULL,
                algorithm TEXT NOT NULL,
                nonce TEXT NOT NULL,
                tag TEXT NOT NULL,
                ciphertext TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """, cancellationToken).ConfigureAwait(false);

        await ExecuteNonQueryAsync(connection, null, """
            CREATE INDEX IF NOT EXISTS ix_ssh_credentials_credential_reference
            ON ssh_credentials(credential_reference COLLATE NOCASE);
            """, cancellationToken).ConfigureAwait(false);

        await EnsureLegacySchemaMigrationAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SshCredentialVaultEntry?> LoadEntryForUpdateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT name, credential_reference, description, created_at_utc, updated_at_utc
            FROM ssh_credentials
            WHERE name = $name;
            """;
        command.Parameters.AddWithValue("$name", name);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new SshCredentialVaultEntry(
            reader.GetString(0),
            reader.GetString(1),
            ReadNullableString(reader, 2),
            DateTimeOffset.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    private static async ValueTask<SecretRow?> LoadSecretByReferenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string credentialReference,
        CancellationToken cancellationToken)
    {
        var normalized = credentialReference.Trim();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT algorithm, nonce, tag, ciphertext
            FROM ssh_credentials
            WHERE name = $reference COLLATE NOCASE
               OR credential_reference = $reference COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$reference", normalized);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new SecretRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }

    private async ValueTask<SshCredentialSecret> ProtectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string databasePath,
        string secret,
        CancellationToken cancellationToken)
    {
        var masterKey = await LoadOrCreateMasterKeyAsync(connection, transaction, databasePath, cancellationToken).ConfigureAwait(false);
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
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    private async ValueTask<string> UnprotectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string databasePath,
        SecretRow secret,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(secret.Algorithm, AlgorithmName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported SSH credential algorithm '{secret.Algorithm}'. Expected '{AlgorithmName}'.");
        }

        var masterKey = await LoadOrCreateMasterKeyAsync(connection, transaction, databasePath, cancellationToken).ConfigureAwait(false);
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
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static async ValueTask<byte[]> LoadOrCreateMasterKeyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string databasePath,
        CancellationToken cancellationToken)
    {
        var existing = await LoadMasterKeyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var gate = MasterKeyLocks.GetOrAdd(databasePath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = await LoadMasterKeyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var masterKey = RandomNumberGenerator.GetBytes(MasterKeyLengthBytes);
                var now = DateTimeOffset.UtcNow.ToString("O");

                try
                {
                    var inserted = await ExecuteNonQueryAsync(connection, transaction, """
                        INSERT INTO ssh_vault_master_keys (
                            name,
                            version,
                            algorithm,
                            master_key,
                            created_at_utc,
                            updated_at_utc)
                        VALUES (
                            $name,
                            $version,
                            $algorithm,
                            $masterKey,
                            $createdAtUtc,
                            $updatedAtUtc);
                        """, cancellationToken,
                        new SqliteParameterValue("$name", MasterKeyName),
                        new SqliteParameterValue("$version", SchemaVersion),
                        new SqliteParameterValue("$algorithm", AlgorithmName),
                        new SqliteParameterValue("$masterKey", Convert.ToBase64String(masterKey)),
                        new SqliteParameterValue("$createdAtUtc", now),
                        new SqliteParameterValue("$updatedAtUtc", now)).ConfigureAwait(false);

                    if (inserted > 0)
                    {
                        return masterKey;
                    }
                }
                catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
                {
                    var reloaded = await LoadMasterKeyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                    if (reloaded is not null)
                    {
                        CryptographicOperations.ZeroMemory(masterKey);
                        return reloaded;
                    }
                }

                CryptographicOperations.ZeroMemory(masterKey);
            }

            throw new InvalidOperationException("Failed to initialize the SSH vault master key.");
        }
        finally
        {
            gate.Release();
        }
    }

    private static async ValueTask<byte[]?> LoadMasterKeyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT master_key
            FROM ssh_vault_master_keys
            WHERE name = $name;
            """;
        command.Parameters.AddWithValue("$name", MasterKeyName);

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string encoded && !string.IsNullOrWhiteSpace(encoded)
            ? Convert.FromBase64String(encoded)
            : null;
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

    private static async ValueTask InsertEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        string credentialReference,
        string? description,
        SshCredentialSecret secret,
        DateTimeOffset createdUtc,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, transaction, """
            INSERT INTO ssh_credentials (
                name,
                credential_reference,
                description,
                algorithm,
                nonce,
                tag,
                ciphertext,
                created_at_utc,
                updated_at_utc)
            VALUES (
                $name,
                $credentialReference,
                $description,
                $algorithm,
                $nonce,
                $tag,
                $ciphertext,
                $createdAtUtc,
                $updatedAtUtc);
            """, cancellationToken,
            new SqliteParameterValue("$name", name),
            new SqliteParameterValue("$credentialReference", credentialReference),
            new SqliteParameterValue("$description", description),
            new SqliteParameterValue("$algorithm", secret.Algorithm),
            new SqliteParameterValue("$nonce", secret.Nonce),
            new SqliteParameterValue("$tag", secret.Tag),
            new SqliteParameterValue("$ciphertext", secret.Ciphertext),
            new SqliteParameterValue("$createdAtUtc", createdUtc.ToString("O")),
            new SqliteParameterValue("$updatedAtUtc", updatedUtc.ToString("O"))).ConfigureAwait(false);
    }

    private static async ValueTask<int> UpdateEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        string credentialReference,
        string? description,
        SshCredentialSecret secret,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken)
    {
        var affected = await ExecuteNonQueryAsync(connection, transaction, """
            UPDATE ssh_credentials
            SET credential_reference = $credentialReference,
                description = $description,
                algorithm = $algorithm,
                nonce = $nonce,
                tag = $tag,
                ciphertext = $ciphertext,
                updated_at_utc = $updatedAtUtc
            WHERE name = $name;
            """, cancellationToken,
            new SqliteParameterValue("$name", name),
            new SqliteParameterValue("$credentialReference", credentialReference),
            new SqliteParameterValue("$description", description),
            new SqliteParameterValue("$algorithm", secret.Algorithm),
            new SqliteParameterValue("$nonce", secret.Nonce),
            new SqliteParameterValue("$tag", secret.Tag),
            new SqliteParameterValue("$ciphertext", secret.Ciphertext),
            new SqliteParameterValue("$updatedAtUtc", updatedUtc.ToString("O"))).ConfigureAwait(false);
        return affected;
    }

    private static async ValueTask EnsureLegacySchemaMigrationAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, "ssh_credentials", "credential_reference", "TEXT NULL", cancellationToken).ConfigureAwait(false);

        if (await HasColumnAsync(connection, "ssh_credentials", "environment_variable", cancellationToken).ConfigureAwait(false))
        {
            await ExecuteNonQueryAsync(connection, null, """
                UPDATE ssh_credentials
                SET credential_reference = COALESCE(credential_reference, environment_variable)
                WHERE credential_reference IS NULL;
                """, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (await HasColumnAsync(connection, tableName, columnName, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await ExecuteNonQueryAsync(connection, null, $"""
            ALTER TABLE {tableName}
            ADD COLUMN {columnName} {columnDefinition};
            """, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<bool> HasColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("SSH credential name is required.", nameof(name));
        }

        return name.Trim();
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static bool IsUniqueConstraintViolation(SqliteException ex)
    {
        return ex.SqliteErrorCode == 19 ||
            ex.SqliteExtendedErrorCode is 1555 or 2067;
    }

    private readonly record struct SqliteParameterValue(string Name, object? Value);

    private readonly record struct SecretRow(string Algorithm, string Nonce, string Tag, string Ciphertext);
}

public static class SshCredentialReference
{
    private const string ProfilePrefix = "ssh/profile/";

    public static string BuildProfileCredentialReference(string profileName, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var profile = NormalizeSegment(profileName);
        var normalizedPurpose = NormalizeSegment(purpose);
        return $"{ProfilePrefix}{profile}/{normalizedPurpose}";
    }

    private static string NormalizeSegment(string value)
    {
        var trimmed = value.Trim();
        var buffer = new char[trimmed.Length];
        var index = 0;
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                buffer[index++] = char.ToLowerInvariant(ch);
            }
            else
            {
                buffer[index++] = '_';
            }
        }

        return new string(buffer, 0, index);
    }
}
