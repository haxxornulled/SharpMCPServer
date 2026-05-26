namespace MCPServer.Infrastructure.Mcp.Stdio;

public readonly struct StdioReadResult
{
    private StdioReadResult(PooledByteBuffer? frame, bool isEndOfInput, bool isTooLarge, bool isInvalidFrame)
    {
        Frame = frame;
        IsEndOfInput = isEndOfInput;
        IsTooLarge = isTooLarge;
        IsInvalidFrame = isInvalidFrame;
    }

    public PooledByteBuffer? Frame { get; }

    public bool IsEndOfInput { get; }

    public bool IsTooLarge { get; }

    public bool IsInvalidFrame { get; }

    public static StdioReadResult Success(PooledByteBuffer frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new StdioReadResult(frame, isEndOfInput: false, isTooLarge: false, isInvalidFrame: false);
    }

    public static StdioReadResult EndOfInput()
    {
        return new StdioReadResult(default, isEndOfInput: true, isTooLarge: false, isInvalidFrame: false);
    }

    public static StdioReadResult TooLarge()
    {
        return new StdioReadResult(default, isEndOfInput: false, isTooLarge: true, isInvalidFrame: false);
    }

    public static StdioReadResult InvalidFrame()
    {
        return new StdioReadResult(default, isEndOfInput: false, isTooLarge: false, isInvalidFrame: true);
    }
}
