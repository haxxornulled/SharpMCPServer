using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Infrastructure.Mcp.Stdio;

public interface IStdioMcpClientFeatureTransport
{
    void Attach(Stream output, IJsonRpcResponseSerializer serializer);

    bool TryHandleResponse(JsonRpcMessage message);
}
