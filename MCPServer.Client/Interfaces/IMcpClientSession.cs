using System.Text.Json;
using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Client.Interfaces;

public interface IMcpClientSession : IAsyncDisposable
{
    ValueTask<Fin<InitializeResult>> InitializeAsync(CancellationToken cancellationToken);

    ValueTask<Fin<ToolsListResult>> ListToolsAsync(string? cursor, CancellationToken cancellationToken);

    ValueTask<Fin<ToolCallResult>> CallToolAsync(string name, JsonElement? arguments, CancellationToken cancellationToken);
}
