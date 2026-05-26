using System.Text.Json;
using LanguageExt;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpMethodHandler
{
    string Method { get; }

    ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken);
}
