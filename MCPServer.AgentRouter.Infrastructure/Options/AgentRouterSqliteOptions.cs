namespace MCPServer.AgentRouter.Infrastructure.Options;

public sealed class AgentRouterSqliteOptions
{
    public const string DefaultConnectionString = "Data Source=agent-router.db;Cache=Shared";

    public string ConnectionString { get; init; } = DefaultConnectionString;

    public bool EnsureCreatedOnUse { get; init; } = true;

    public static AgentRouterSqliteOptions Default { get; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new ArgumentException("AgentRouter SQLite connection string is required.", nameof(ConnectionString));
        }
    }
}
