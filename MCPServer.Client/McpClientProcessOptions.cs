namespace MCPServer.Client;

public sealed class McpClientProcessOptions
{
    public string ServerExecutablePath { get; init; } = string.Empty;

    public IReadOnlyList<string> ServerArguments { get; init; } = Array.Empty<string>();

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public string ClientName { get; init; } = "mcpserver-client-console";

    public string ClientTitle { get; init; } = "MCP Server Client Console";

    public string ClientVersion { get; init; } = "1.0.0";

    public int MaxInputFrameBytes { get; init; } = 1_048_576;
}
