namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public sealed class McpNullHttpAuthorizationService : IMcpHttpAuthorizationService
{
    public ValueTask<McpHttpAuthorizationDecision> AuthorizeAsync(StreamableHttpMcpRequestEnvelope request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(McpHttpAuthorizationDecision.Authorized());
    }
}
