namespace MCPServer.Client.StreamableHttp;

internal sealed class McpSseEvent
{
    public string? Id { get; init; }

    public int? RetryMilliseconds { get; init; }

    public string Data { get; init; } = string.Empty;
}
