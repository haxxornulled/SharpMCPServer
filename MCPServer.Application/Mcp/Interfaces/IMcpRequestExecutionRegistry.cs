using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpRequestExecutionRegistry
{
    Fin<McpRequestExecutionScope> Register(JsonRpcMessage message, CancellationToken transportCancellationToken);

    bool TryCancel(JsonRpcRequestId requestId, string? reason);

    void Reset();
}
