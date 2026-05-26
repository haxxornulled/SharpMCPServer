using LanguageExt;
using MCPServer.Client.Models;
using MCPServer.Domain.Mcp;

namespace MCPServer.Client.Interfaces;

public interface IMcpClientSamplingHandler
{
    ValueTask<Fin<McpClientSamplingResponse>> HandleAsync(
        CreateMessageRequestParams parameters,
        IMcpClientTaskRegistry taskRegistry,
        CancellationToken cancellationToken);
}
