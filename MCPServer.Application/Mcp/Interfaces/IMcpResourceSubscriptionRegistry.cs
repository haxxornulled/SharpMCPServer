using LanguageExt;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpResourceSubscriptionRegistry
{
    Fin<bool> Subscribe(string uri);

    Fin<bool> Unsubscribe(string uri);

    bool IsSubscribed(string uri);
}
