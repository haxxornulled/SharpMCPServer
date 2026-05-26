using System.Net;
using LanguageExt;

namespace MCPServer.Infrastructure.Mcp.Http;

public interface IStreamableHttpMcpSessionManager : IDisposable
{
    Fin<StreamableHttpMcpSessionContext> CreateSession();

    bool TryGetSession(string sessionId, out StreamableHttpMcpSessionContext? session);

    bool TryTerminateSession(string sessionId);

    bool TryValidateSessionRequest(
        StreamableHttpMcpRequest request,
        bool isInitialize,
        out StreamableHttpMcpSessionContext? session,
        out HttpStatusCode statusCode,
        out string errorMessage);
}
