using System.Net;

namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public sealed class McpHttpAuthorizationDecision
{
    private McpHttpAuthorizationDecision()
    {
    }

    public bool IsAuthorized { get; private init; }

    public HttpStatusCode StatusCode { get; private init; } = HttpStatusCode.OK;

    public string? WwwAuthenticate { get; private init; }

    public string? ErrorMessage { get; private init; }

    public static McpHttpAuthorizationDecision Authorized()
    {
        return new McpHttpAuthorizationDecision
        {
            IsAuthorized = true,
            StatusCode = HttpStatusCode.OK
        };
    }

    public static McpHttpAuthorizationDecision Denied(HttpStatusCode statusCode, string wwwAuthenticate, string errorMessage)
    {
        if (statusCode == HttpStatusCode.OK)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode), "Denied authorization decisions must not use HTTP 200 OK.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(wwwAuthenticate);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new McpHttpAuthorizationDecision
        {
            IsAuthorized = false,
            StatusCode = statusCode,
            WwwAuthenticate = wwwAuthenticate,
            ErrorMessage = errorMessage
        };
    }
}
