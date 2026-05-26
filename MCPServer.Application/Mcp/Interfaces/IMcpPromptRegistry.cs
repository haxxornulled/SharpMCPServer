using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpPromptRegistry
{
    Fin<PromptsListResult> ListPrompts(string? cursor);

    Fin<IMcpPrompt> FindPrompt(string name);
}
