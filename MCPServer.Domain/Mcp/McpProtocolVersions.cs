namespace MCPServer.Domain.Mcp;

public static class McpProtocolVersions
{
    public const string V2025_11_25 = "2025-11-25";
    public const string Current = V2025_11_25;

    public static bool IsSupported(string? protocolVersion)
    {
        return string.Equals(protocolVersion, V2025_11_25, StringComparison.Ordinal);
    }
}
