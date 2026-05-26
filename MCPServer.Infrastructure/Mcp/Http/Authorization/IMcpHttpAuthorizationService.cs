namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public interface IMcpHttpAuthorizationService
{
    ValueTask<McpHttpAuthorizationDecision> AuthorizeAsync(StreamableHttpMcpRequestEnvelope request, CancellationToken cancellationToken);
}
