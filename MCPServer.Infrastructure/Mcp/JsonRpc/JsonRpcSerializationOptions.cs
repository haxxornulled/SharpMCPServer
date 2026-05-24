namespace MCPServer.Infrastructure.Mcp.JsonRpc;

public sealed class JsonRpcSerializationOptions
{
    public int InitialBufferBytes { get; init; } = 512;

    public int MaxOutputFrameBytes { get; init; } = 1_048_576;

    public bool ValidateNoEmbeddedNewlines { get; init; } = true;

    public bool ClearReturnedOutputBuffers { get; init; } = true;
}
