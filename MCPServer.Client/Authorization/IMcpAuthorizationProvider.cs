using LanguageExt;

namespace MCPServer.Client.Authorization;

public interface IMcpAuthorizationProvider
{
    ValueTask<Fin<string>> GetAccessTokenAsync(McpAuthorizationContext context, CancellationToken cancellationToken);
}
