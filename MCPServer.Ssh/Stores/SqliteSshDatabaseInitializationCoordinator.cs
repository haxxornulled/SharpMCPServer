using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace MCPServer.Ssh.Stores;

internal static class SqliteSshDatabaseInitializationCoordinator
{
    private static readonly ConcurrentDictionary<string, PathInitializationState> States = new(StringComparer.OrdinalIgnoreCase);

    public static async ValueTask EnsureInitializedAsync(
        string databasePath,
        string schemaName,
        SqliteConnection connection,
        Func<SqliteConnection, CancellationToken, ValueTask> initializeAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(initializeAsync);

        var normalizedPath = Path.GetFullPath(databasePath);
        var state = States.GetOrAdd(normalizedPath, static _ => new PathInitializationState());

        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (state.InitializedSchemas.Contains(schemaName))
            {
                return;
            }

            await initializeAsync(connection, cancellationToken).ConfigureAwait(false);
            state.InitializedSchemas.Add(schemaName);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private sealed class PathInitializationState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public HashSet<string> InitializedSchemas { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
