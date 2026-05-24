using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpToolRegistry
{
    Fin<ToolsListResult> ListTools(string? cursor);

    Fin<IMcpTool> FindTool(string name);
}
