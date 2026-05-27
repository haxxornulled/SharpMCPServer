using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace MCPServer.Workspace.Stores;

internal static class SqliteWorkspaceDatabaseInitializationCoordinator
{
    private static readonly ConcurrentDictionary<string, PathInitializationState> States = new(StringComparer.OrdinalIgnoreCase);

    public static void EnsureInitialized(
        string databasePath,
        string schemaName,
        SqliteConnection connection,
        Action<SqliteConnection> initialize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(initialize);

        var normalizedPath = Path.GetFullPath(databasePath);
        var state = States.GetOrAdd(normalizedPath, static _ => new PathInitializationState());

        state.Gate.Wait();
        try
        {
            if (state.InitializedSchemas.Contains(schemaName))
            {
                return;
            }

            initialize(connection);
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
