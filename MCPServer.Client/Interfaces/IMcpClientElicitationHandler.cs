using LanguageExt;
using MCPServer.Client.Models;
using MCPServer.Domain.Mcp;

namespace MCPServer.Client.Interfaces;

public interface IMcpClientElicitationHandler
{
    ValueTask<Fin<McpClientElicitationResponse>> HandleFormAsync(
        ElicitRequestFormParams parameters,
        IMcpClientTaskRegistry taskRegistry,
        CancellationToken cancellationToken);

    ValueTask<Fin<McpClientElicitationResponse>> HandleUrlAsync(
        ElicitRequestUrlParams parameters,
        IMcpClientTaskRegistry taskRegistry,
        CancellationToken cancellationToken);
}
