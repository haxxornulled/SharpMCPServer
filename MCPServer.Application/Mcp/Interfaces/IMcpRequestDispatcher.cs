using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpRequestDispatcher
{
    ValueTask<Fin<JsonRpcDispatchResult>> DispatchAsync(JsonRpcMessage message, CancellationToken cancellationToken);
}
