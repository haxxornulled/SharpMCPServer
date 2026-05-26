using System.Security.Claims;

namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public sealed class McpAccessTokenValidationResult
{
    public required ClaimsPrincipal Principal { get; init; }

    public required string Issuer { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();
}
