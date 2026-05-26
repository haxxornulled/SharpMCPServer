using System.Text.Json;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class PingHandler : IMcpMethodHandler
{
    public string Method => McpMethods.Ping;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (parameters is { } suppliedParameters && suppliedParameters is not { ValueKind: JsonValueKind.Object })
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New("ping parameters must be a JSON object when supplied.")));
        }

        return new ValueTask<Fin<JsonElement>>(Fin.Succ<JsonElement>(McpJsonElements.EmptyObject));
    }
}
