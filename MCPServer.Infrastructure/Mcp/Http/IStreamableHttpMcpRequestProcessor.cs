namespace MCPServer.Infrastructure.Mcp.Http;

public interface IStreamableHttpMcpRequestProcessor
{
    ValueTask<StreamableHttpMcpResponse> ProcessAsync(StreamableHttpMcpRequest request, CancellationToken cancellationToken);
}
