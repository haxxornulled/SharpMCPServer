using LanguageExt;
using MCPServer.Domain.Mcp;
using System.Text.Json;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpCompletionReferenceParser
{
    Fin<McpCompletionReference> Parse(JsonElement reference);
}
