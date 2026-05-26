using LanguageExt;

namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public interface IMcpAccessTokenValidator
{
    ValueTask<Fin<McpAccessTokenValidationResult>> ValidateAsync(string accessToken, Uri resourceUri, CancellationToken cancellationToken);
}
