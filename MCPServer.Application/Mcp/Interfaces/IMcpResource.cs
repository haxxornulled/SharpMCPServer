using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpResource
{
    McpResourceDescriptor Descriptor { get; }

    ValueTask<Fin<ResourcesReadResult>> ReadAsync(CancellationToken cancellationToken);
}
