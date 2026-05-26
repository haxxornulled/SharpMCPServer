namespace MCPServer.AgentRouter.PythonBridge.Native;

internal static class NativeBridgeComposer
{
    private static readonly NativeBridgeRuntime Runtime = new();

    public static NativeBridgeRuntime GetBridgeRuntime()
    {
        return Runtime;
    }

    public static void Shutdown()
    {
        // The bridge keeps no unmanaged process-wide state yet. This remains a
        // logical lifecycle hook for future resources and Python shutdown parity.
    }
}
