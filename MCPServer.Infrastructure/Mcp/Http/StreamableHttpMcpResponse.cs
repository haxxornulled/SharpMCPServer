using System.Net;

namespace MCPServer.Infrastructure.Mcp.Http;

public sealed class StreamableHttpMcpResponse
{
    public HttpStatusCode StatusCode { get; init; }

    public string? ContentType { get; init; }

    public Dictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public byte[]? Body { get; init; }

    public static StreamableHttpMcpResponse Empty(HttpStatusCode statusCode)
    {
        return new StreamableHttpMcpResponse
        {
            StatusCode = statusCode
        };
    }
}
