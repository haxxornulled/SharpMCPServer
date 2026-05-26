namespace MCPServer.Infrastructure.Mcp.Http;

public sealed class StreamableHttpMcpRequest
{
    public required string Method { get; init; }

    public required Uri RequestUri { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public required ReadOnlyMemory<byte> Body { get; init; }

    public string? GetHeader(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Headers.TryGetValue(name, out var value) ? value : default;
    }
}
