using System.Text.Json;
using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpClientFeatureInvoker
{
    ValueTask<Fin<JsonElement>> CreateMessageAsync(CreateMessageRequestParams parameters, CancellationToken cancellationToken);

    ValueTask<Fin<JsonElement>> ElicitFormAsync(ElicitRequestFormParams parameters, CancellationToken cancellationToken);

    ValueTask<Fin<JsonElement>> ElicitUrlAsync(ElicitRequestUrlParams parameters, CancellationToken cancellationToken);
}
