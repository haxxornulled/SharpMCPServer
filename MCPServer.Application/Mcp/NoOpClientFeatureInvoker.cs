using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp;

public sealed class NoOpClientFeatureInvoker : IMcpClientFeatureInvoker
{
    public ValueTask<Fin<JsonElement>> CreateMessageAsync(CreateMessageRequestParams parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New("The connected client does not support sampling/createMessage over the active transport.")));
    }

    public ValueTask<Fin<JsonElement>> ElicitFormAsync(ElicitRequestFormParams parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New("The connected client does not support elicitation/create form requests over the active transport.")));
    }

    public ValueTask<Fin<JsonElement>> ElicitUrlAsync(ElicitRequestUrlParams parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New("The connected client does not support elicitation/create URL requests over the active transport.")));
    }
}
