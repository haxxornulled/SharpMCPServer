using System.Collections.Concurrent;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp;

public sealed class McpSessionState : IMcpSessionState
{
    private readonly ConcurrentDictionary<string, byte> _clientRequestIds = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
    private int _initializeResponseSent;
    private int _initializedNotificationReceived;
    private string? _negotiatedProtocolVersion;
    private McpImplementationInfo? _clientInfo;
    private McpClientCapabilityState _clientCapabilities = McpClientCapabilityState.Empty;
    private int _rootsListRevision;

    public bool InitializeResponseSent => Volatile.Read(ref _initializeResponseSent) != 0;

    public bool InitializedNotificationReceived => Volatile.Read(ref _initializedNotificationReceived) != 0;

    public string? NegotiatedProtocolVersion => Volatile.Read(ref _negotiatedProtocolVersion);

    public McpImplementationInfo? ClientInfo => Volatile.Read(ref _clientInfo);

    public McpClientCapabilityState ClientCapabilities => Volatile.Read(ref _clientCapabilities);

    public int RootsListRevision => Volatile.Read(ref _rootsListRevision);

    public bool TryRegisterClientRequestId(JsonRpcRequestId requestId)
    {
        if (!requestId.TryGetStableKey(out var key))
        {
            return false;
        }

        return _clientRequestIds.TryAdd(key, 0);
    }

    public void MarkInitializeResponseSent(string protocolVersion, McpImplementationInfo clientInfo, McpClientCapabilityState clientCapabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolVersion);
        ArgumentNullException.ThrowIfNull(clientInfo);
        ArgumentNullException.ThrowIfNull(clientCapabilities);

        Volatile.Write(ref _negotiatedProtocolVersion, protocolVersion);
        Volatile.Write(ref _clientInfo, clientInfo);
        Volatile.Write(ref _clientCapabilities, clientCapabilities);
        Volatile.Write(ref _initializeResponseSent, 1);
    }

    public void MarkInitializedNotificationReceived()
    {
        Volatile.Write(ref _initializedNotificationReceived, 1);
    }

    public void MarkRootsListChanged()
    {
        Interlocked.Increment(ref _rootsListRevision);
    }
}

