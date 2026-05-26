using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Interfaces;

public interface IMcpSessionState
{
    bool InitializeResponseSent { get; }

    bool InitializedNotificationReceived { get; }

    string? NegotiatedProtocolVersion { get; }

    McpImplementationInfo? ClientInfo { get; }

    McpClientCapabilityState ClientCapabilities { get; }

    int RootsListRevision { get; }

    bool TryRegisterClientRequestId(JsonRpcRequestId requestId);

    void MarkInitializeResponseSent(string protocolVersion, McpImplementationInfo clientInfo, McpClientCapabilityState clientCapabilities);

    void MarkInitializedNotificationReceived();

    void MarkRootsListChanged();

    void ResetSession();
}
