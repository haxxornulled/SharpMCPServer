using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpResourceRegistry
{
    Fin<ResourcesListResult> ListResources(string? cursor);

    Fin<ResourceTemplatesListResult> ListResourceTemplates();

    Fin<IMcpResource> FindResource(string uri);
}
