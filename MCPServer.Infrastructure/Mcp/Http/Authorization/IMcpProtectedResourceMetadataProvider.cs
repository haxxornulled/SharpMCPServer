namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public interface IMcpProtectedResourceMetadataProvider
{
    bool IsProtectedResourceMetadataRequest(Uri requestUri);

    Uri GetResourceMetadataUri(Uri requestUri);

    McpProtectedResourceMetadataDocument CreateDocument(Uri requestUri);
}
