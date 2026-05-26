using System.Net;

namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public sealed class StreamableHttpMcpRequestEnvelope
{
    public required string Method { get; init; }

    public required Uri RequestUri { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public string? GetHeader(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Headers.TryGetValue(name, out var value) ? value : default;
    }
}
