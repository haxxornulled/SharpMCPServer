namespace MCPServer.Infrastructure.Mcp.Stdio;

public sealed class StdioMcpTransportOptions
{
    public bool Enabled { get; init; } = true;

    public int MaxInputFrameBytes { get; init; } = 1_048_576;

    public int ReadBufferBytes { get; init; } = 16 * 1024;

    public int InitialFrameBufferBytes { get; init; } = 4 * 1024;

    public bool ClearReturnedInputBuffers { get; init; } = true;

    public bool AllowFinalFrameWithoutNewline { get; init; }
}
