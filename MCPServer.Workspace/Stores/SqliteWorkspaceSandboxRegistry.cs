using LanguageExt;
using LanguageExt.Common;
using MCPServer.Workspace.Configuration;
using MCPServer.Workspace.Models;
using Microsoft.Data.Sqlite;

namespace MCPServer.Workspace.Stores;

public sealed class SqliteWorkspaceSandboxRegistry
{
    private const string SchemaName = "workspace-sandboxes";
    private const string StatusCreating = "creating";
    private const string StatusReady = "ready";
    private const string StatusDeleting = "deleting";

    private readonly McpWorkspaceOptions _options;

    public SqliteWorkspaceSandboxRegistry(McpWorkspaceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public IReadOnlyList<WorkspaceRoot> GetSandboxes()
    {
        using var connection = OpenInitializedConnection();
        return LoadReadySandboxes(connection);
    }

    public Fin<WorkspaceRoot> FindSandbox(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fin.Fail<WorkspaceRoot>(Error.New("Workspace sandbox name is required."));
        }

        try
        {
            using var connection = OpenInitializedConnection();
            var sandbox = LoadReadySandbox(connection, NormalizeSandboxName(name));
            return sandbox is null
                ? Fin.Fail<WorkspaceRoot>(Error.New($"Workspace sandbox '{name}' was not found."))
                : Fin.Succ(sandbox);
        }
        catch (Exception ex)
        {
            return Fin.Fail<WorkspaceRoot>(Error.New($"Failed to load workspace sandbox '{name}': {ex.Message}"));
        }
    }

    public ValueTask<Fin<WorkspaceRoot>> CreateAsync(
        WorkspaceRoot source,
        string sandboxName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (string.IsNullOrWhiteSpace(source.Name))
            {
                return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New("Workspace source root name is required.")));
            }

            if (string.IsNullOrWhiteSpace(source.Path))
            {
                return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New($"Workspace source root '{source.Name}' does not have a path.")));
            }

            var key = NormalizeSandboxName(sandboxName);
            var basePath = ResolveSandboxBasePath();
            var sandboxPath = Path.Combine(basePath, key);

            if (Directory.Exists(sandboxPath))
            {
                return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New($"Workspace sandbox path '{sandboxPath}' already exists.")));
            }

            Directory.CreateDirectory(basePath);

            using (var connection = OpenInitializedConnection())
            {
                if (!TryReserveSandboxRow(connection, key, source, sandboxPath, cancellationToken))
                {
                    return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New($"Workspace sandbox '{key}' is already in use.")));
                }
            }

            try
            {
                CopyDirectory(source.Path, sandboxPath, _options.ExcludedDirectoryNames);
            }
            catch (Exception ex)
            {
                TryDeleteDirectory(sandboxPath);
                DeleteSandboxRow(key);
                return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New($"Workspace sandbox '{key}' could not be created: {ex.Message}")));
            }

            var now = DateTimeOffset.UtcNow;
            var sandbox = new WorkspaceRoot
            {
                Name = key,
                Path = sandboxPath,
                Kind = "sandbox",
                AllowWrite = true,
                Exists = true,
                SourceRootName = source.Name.Trim(),
                CreatedUtc = now
            };

            try
            {
                using var connection = OpenInitializedConnection();
                using var transaction = connection.BeginTransaction();

                var affected = ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE workspace_sandboxes
                    SET source_root_name = $sourceRootName,
                        source_root_path = $sourceRootPath,
                        sandbox_path = $sandboxPath,
                        kind = 'sandbox',
                        allow_write = 1,
                        is_present = 1,
                        status = $status,
                        updated_at_utc = $updatedAtUtc
                    WHERE sandbox_name = $sandboxName AND status = $creatingStatus;
                    """,
                    new SqliteParameterValue("$sourceRootName", source.Name.Trim()),
                    new SqliteParameterValue("$sourceRootPath", Path.GetFullPath(source.Path.Trim(), AppContext.BaseDirectory)),
                    new SqliteParameterValue("$sandboxPath", sandboxPath),
                    new SqliteParameterValue("$status", StatusReady),
                    new SqliteParameterValue("$updatedAtUtc", now.ToString("O")),
                    new SqliteParameterValue("$sandboxName", key),
                    new SqliteParameterValue("$creatingStatus", StatusCreating));

                if (affected == 0)
                {
                    throw new InvalidOperationException($"Workspace sandbox '{key}' could not be finalized.");
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                TryDeleteDirectory(sandboxPath);
                DeleteSandboxRow(key);
                return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New($"Workspace sandbox '{key}' could not be finalized: {ex.Message}")));
            }

            return ValueTask.FromResult(Fin.Succ(sandbox));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New($"Failed to create workspace sandbox: {ex.Message}")));
        }
    }

    public ValueTask<Fin<WorkspaceRoot>> DeleteAsync(string sandboxName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var key = NormalizeSandboxName(sandboxName);
            using var connection = OpenInitializedConnection();
            using var transaction = connection.BeginTransaction();

            var sandbox = LoadSandboxForUpdate(connection, transaction, key);
            if (sandbox is null)
            {
                return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New($"Workspace sandbox '{sandboxName}' was not found.")));
            }

            var now = DateTimeOffset.UtcNow;
            var markDeleting = ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE workspace_sandboxes
                SET status = $status,
                    is_present = 0,
                    updated_at_utc = $updatedAtUtc
                WHERE sandbox_name = $sandboxName AND status = $readyStatus;
                """,
                new SqliteParameterValue("$status", StatusDeleting),
                new SqliteParameterValue("$updatedAtUtc", now.ToString("O")),
                new SqliteParameterValue("$sandboxName", key),
                new SqliteParameterValue("$readyStatus", StatusReady));

            if (markDeleting == 0)
            {
                return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New($"Workspace sandbox '{sandboxName}' is not ready for deletion.")));
            }

            transaction.Commit();

            try
            {
                if (Directory.Exists(sandbox.Path))
                {
                    Directory.Delete(sandbox.Path, recursive: true);
                }
            }
            catch (Exception ex)
            {
                RestoreReadyState(key);
                return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New($"Workspace sandbox '{sandboxName}' could not be deleted: {ex.Message}")));
            }

            try
            {
                DeleteSandboxRow(key);
            }
            catch (Exception ex)
            {
                return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New($"Workspace sandbox '{sandboxName}' directory was removed, but registry cleanup failed: {ex.Message}")));
            }

            sandbox.Exists = false;
            sandbox.Kind = "sandbox";
            return ValueTask.FromResult(Fin.Succ(sandbox));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(Error.New($"Failed to delete workspace sandbox: {ex.Message}")));
        }
    }

    private bool TryReserveSandboxRow(
        SqliteConnection connection,
        string sandboxName,
        WorkspaceRoot source,
        string sandboxPath,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();

        try
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            var affected = ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO workspace_sandboxes (
                    sandbox_name,
                    source_root_name,
                    source_root_path,
                    sandbox_path,
                    kind,
                    allow_write,
                    is_present,
                    created_at_utc,
                    updated_at_utc,
                    status)
                VALUES (
                    $sandboxName,
                    $sourceRootName,
                    $sourceRootPath,
                    $sandboxPath,
                    'sandbox',
                    1,
                    0,
                    $createdAtUtc,
                    $updatedAtUtc,
                    $status);
                """,
                new SqliteParameterValue("$sandboxName", sandboxName),
                new SqliteParameterValue("$sourceRootName", source.Name.Trim()),
                new SqliteParameterValue("$sourceRootPath", Path.GetFullPath(source.Path.Trim(), AppContext.BaseDirectory)),
                new SqliteParameterValue("$sandboxPath", sandboxPath),
                new SqliteParameterValue("$createdAtUtc", now),
                new SqliteParameterValue("$updatedAtUtc", now),
                new SqliteParameterValue("$status", StatusCreating));

            transaction.Commit();
            return affected > 0;
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
        {
            transaction.Rollback();
            if (TryReclaimStaleSandboxName(connection, sandboxName))
            {
                return TryReserveSandboxRow(connection, sandboxName, source, sandboxPath, cancellationToken);
            }

            return false;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private bool TryReclaimStaleSandboxName(SqliteConnection connection, string sandboxName)
    {
        var sandbox = LoadSandboxAnyStatus(connection, sandboxName);
        if (sandbox is null)
        {
            DeleteSandboxRow(sandboxName);
            return true;
        }

        if (sandbox.Exists && Directory.Exists(sandbox.Path))
        {
            return false;
        }

        DeleteSandboxRow(sandboxName);
        TryDeleteDirectory(sandbox.Path);
        return true;
    }

    private IReadOnlyList<WorkspaceRoot> LoadReadySandboxes(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sandbox_name, source_root_name, source_root_path, sandbox_path, kind, allow_write, is_present, created_at_utc, updated_at_utc
            FROM workspace_sandboxes
            WHERE status = $status
            ORDER BY sandbox_name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$status", StatusReady);

        var sandboxes = new List<WorkspaceRoot>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sandboxes.Add(ReadSandbox(reader));
        }

        return sandboxes;
    }

    private WorkspaceRoot? LoadSandboxForUpdate(SqliteConnection connection, SqliteTransaction transaction, string sandboxName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT sandbox_name, source_root_name, source_root_path, sandbox_path, kind, allow_write, is_present, created_at_utc, updated_at_utc
            FROM workspace_sandboxes
            WHERE sandbox_name = $sandboxName AND status = $status
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sandboxName", sandboxName);
        command.Parameters.AddWithValue("$status", StatusReady);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSandbox(reader) : null;
    }

    private WorkspaceRoot? LoadSandboxAnyStatus(SqliteConnection connection, string sandboxName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sandbox_name, source_root_name, source_root_path, sandbox_path, kind, allow_write, is_present, created_at_utc, updated_at_utc
            FROM workspace_sandboxes
            WHERE sandbox_name = $sandboxName
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sandboxName", sandboxName);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSandbox(reader) : null;
    }

    private WorkspaceRoot? LoadReadySandbox(SqliteConnection connection, string sandboxName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sandbox_name, source_root_name, source_root_path, sandbox_path, kind, allow_write, is_present, created_at_utc, updated_at_utc
            FROM workspace_sandboxes
            WHERE sandbox_name = $sandboxName AND status = $status
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sandboxName", sandboxName);
        command.Parameters.AddWithValue("$status", StatusReady);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSandbox(reader) : null;
    }

    private static WorkspaceRoot ReadSandbox(SqliteDataReader reader)
    {
        return new WorkspaceRoot
        {
            Name = reader.GetString(0),
            SourceRootName = ReadNullableString(reader, 1),
            Path = reader.GetString(3),
            Kind = string.IsNullOrWhiteSpace(reader.GetString(4)) ? "sandbox" : reader.GetString(4),
            AllowWrite = ReadBoolean(reader, 5),
            Exists = ReadBoolean(reader, 6) && Directory.Exists(reader.GetString(3)),
            CreatedUtc = DateTimeOffset.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    private void RestoreReadyState(string sandboxName)
    {
        using var connection = OpenInitializedConnection();
        using var transaction = connection.BeginTransaction();
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE workspace_sandboxes
            SET status = $status,
                is_present = 1,
                updated_at_utc = $updatedAtUtc
            WHERE sandbox_name = $sandboxName;
            """,
            new SqliteParameterValue("$status", StatusReady),
            new SqliteParameterValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O")),
            new SqliteParameterValue("$sandboxName", sandboxName));
        transaction.Commit();
    }

    private void DeleteSandboxRow(string sandboxName)
    {
        using var connection = OpenInitializedConnection();
        using var transaction = connection.BeginTransaction();
        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM workspace_sandboxes WHERE sandbox_name = $sandboxName;",
            new SqliteParameterValue("$sandboxName", sandboxName));
        transaction.Commit();
    }

    private SqliteConnection OpenInitializedConnection()
    {
        var databasePath = ResolveDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? Directory.GetCurrentDirectory());

        var connection = new SqliteConnection(BuildConnectionString(databasePath));
        connection.Open();
        ConfigureConnection(connection);

        if (_options.Sqlite.EnsureCreatedOnUse)
        {
            SqliteWorkspaceDatabaseInitializationCoordinator.EnsureInitialized(
                databasePath,
                SchemaName,
                connection,
                connection => InitializeSchema(connection));
        }

        return connection;
    }

    private string ResolveDatabasePath()
    {
        return _options.Sqlite.DatabasePath;
    }

    private string ResolveSandboxBasePath()
    {
        return _options.SandboxBasePath;
    }

    private static string BuildConnectionString(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
            DefaultTimeout = 30
        };

        return builder.ToString();
    }

    private static void ConfigureConnection(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, null, "PRAGMA foreign_keys = ON;");
        ExecuteNonQuery(connection, null, "PRAGMA busy_timeout = 5000;");
        ExecuteNonQuery(connection, null, "PRAGMA synchronous = NORMAL;");
    }

    private static void InitializeSchema(SqliteConnection connection)
    {
        ExecuteScalar(connection, "PRAGMA journal_mode = WAL;");

        ExecuteNonQuery(connection, null, """
            CREATE TABLE IF NOT EXISTS workspace_sandboxes (
                sandbox_name TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                source_root_name TEXT NOT NULL,
                source_root_path TEXT NOT NULL,
                sandbox_path TEXT NOT NULL,
                kind TEXT NOT NULL,
                allow_write INTEGER NOT NULL,
                is_present INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                status TEXT NOT NULL
            );
            """);

        ExecuteNonQuery(connection, null, """
            CREATE INDEX IF NOT EXISTS ix_workspace_sandboxes_status
            ON workspace_sandboxes(status, sandbox_name COLLATE NOCASE);
            """);
    }

    private static int ExecuteNonQuery(SqliteConnection connection, SqliteTransaction? transaction, string commandText, params SqliteParameterValue[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = transaction;

        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        return command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return command.ExecuteScalar();
    }

    private static bool IsUniqueConstraintViolation(SqliteException ex)
    {
        return ex.SqliteErrorCode == 19 ||
            ex.SqliteExtendedErrorCode is 1555 or 2067;
    }

    private static string NormalizeSandboxName(string sandboxName)
    {
        if (string.IsNullOrWhiteSpace(sandboxName))
        {
            throw new InvalidOperationException("Workspace sandbox name is required.");
        }

        var trimmed = sandboxName.Trim();
        if (!IsSafeSegment(trimmed))
        {
            throw new InvalidOperationException("Workspace sandbox name must be a simple filesystem-safe name.");
        }

        return trimmed;
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static bool ReadBoolean(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        return reader.GetFieldType(ordinal) == typeof(long)
            ? reader.GetInt64(ordinal) != 0
            : reader.GetBoolean(ordinal);
    }

    private static bool IsSafeSegment(string value)
    {
        if (value.Length == 0 || value is "." or "..")
        {
            return false;
        }

        var invalid = Path.GetInvalidFileNameChars();
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch is '/' or '\\' or ':' || invalid.Contains(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static void CopyDirectory(string sourcePath, string destinationPath, IReadOnlyCollection<string> excludedDirectoryNames)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var file in Directory.EnumerateFiles(sourcePath))
        {
            var destinationFile = Path.Combine(destinationPath, Path.GetFileName(file));
            File.Copy(file, destinationFile, overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourcePath))
        {
            var directoryName = Path.GetFileName(directory);
            if (ShouldSkipDirectory(directoryName, excludedDirectoryNames))
            {
                continue;
            }

            var destinationDirectory = Path.Combine(destinationPath, Path.GetFileName(directory));
            CopyDirectory(directory, destinationDirectory, excludedDirectoryNames);
        }
    }

    private static bool ShouldSkipDirectory(string? directoryName, IReadOnlyCollection<string> excludedDirectoryNames)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return true;
        }

        foreach (var excluded in excludedDirectoryNames)
        {
            if (!string.IsNullOrWhiteSpace(excluded) && string.Equals(directoryName, excluded.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private readonly record struct SqliteParameterValue(string Name, object? Value);
}
