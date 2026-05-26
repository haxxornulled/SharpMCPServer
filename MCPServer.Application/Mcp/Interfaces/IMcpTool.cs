using System.Text.Json;
using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpTool
{
    McpToolDescriptor Descriptor { get; }

    ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken);
}
