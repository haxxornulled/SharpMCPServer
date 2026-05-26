using System.Text.Json;
using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpPrompt
{
    McpPromptDescriptor Descriptor { get; }

    ValueTask<Fin<PromptsGetResult>> GetAsync(JsonElement? arguments, CancellationToken cancellationToken);
}
