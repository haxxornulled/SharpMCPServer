using LanguageExt;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpPromptCompletionProvider
{
    ValueTask<Fin<CompleteResult>> CompleteAsync(
        CompletionArgument argument,
        CompletionContext? context,
        CancellationToken cancellationToken);
}
