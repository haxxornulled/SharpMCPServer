using System.Text.Json;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.JsonRpc.Interfaces;

public interface IJsonRpcResponseSerializer
{
    ValueTask WriteAsync(Stream output, JsonRpcResponse response, CancellationToken cancellationToken);

    ValueTask WriteNotificationAsync(Stream output, string method, JsonElement? parameters, CancellationToken cancellationToken);
}
