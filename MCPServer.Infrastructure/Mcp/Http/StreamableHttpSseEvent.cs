namespace MCPServer.Infrastructure.Mcp.Http;

public sealed class StreamableHttpSseEvent
{
    public required string Id { get; init; }

    public string Data { get; init; } = string.Empty;

    public int? RetryMilliseconds { get; init; }
}
