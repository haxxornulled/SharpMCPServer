using System.Net;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Infrastructure.Mcp.Http;

public interface IStreamableHttpMcpSessionTransport : IMcpClientFeatureInvoker, IMcpTaskStatusNotifier
{
    string? SessionId { get; }

    bool HasActiveSession { get; }

    void StartSession();

    void TerminateSession();

    bool TryValidateSessionRequest(StreamableHttpMcpRequest request, bool isInitialize, out HttpStatusCode statusCode, out string errorMessage);

    bool TryHandleResponse(JsonRpcMessage message);

    bool TryValidateEventStreamRequest(string? lastEventId, out HttpStatusCode statusCode, out string errorMessage);

    ValueTask OpenEventStreamAsync(Stream output, string? lastEventId, CancellationToken cancellationToken);
}
