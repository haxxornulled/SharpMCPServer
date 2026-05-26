using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.JsonRpc.Interfaces;

public interface IJsonRpcMessageParser
{
    Fin<JsonRpcMessage> Parse(ReadOnlyMemory<byte> json);
}
