using System.Buffers;

namespace MCPServer.Infrastructure.Mcp.Stdio;

public sealed class StdioMcpTransportSession : IDisposable, IAsyncDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly bool _clearReturnedInputBuffers;
    private readonly bool _allowFinalFrameWithoutNewline;
    private byte[]? _readBuffer;
    private int _readOffset;
    private int _readCount;
    private byte[]? _frameBuffer;
    private int _frameLength;
    private bool _discardingOversizedFrame;
    private bool _disposed;

    private StdioMcpTransportSession(
        Stream input,
        Stream output,
        byte[] readBuffer,
        byte[] frameBuffer,
        bool clearReturnedInputBuffers,
        bool allowFinalFrameWithoutNewline)
    {
        _input = input;
        _output = output;
        _readBuffer = readBuffer;
        _frameBuffer = frameBuffer;
        _clearReturnedInputBuffers = clearReturnedInputBuffers;
        _allowFinalFrameWithoutNewline = allowFinalFrameWithoutNewline;
    }

    public Stream Output
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _output;
        }
    }

    public static StdioMcpTransportSession Open(StdioMcpTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Open(Console.OpenStandardInput(), Console.OpenStandardOutput(), options);
    }

    public static StdioMcpTransportSession Open(Stream input, Stream output, StdioMcpTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ReadBufferBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.InitialFrameBufferBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxInputFrameBytes);

        var readBuffer = ArrayPool<byte>.Shared.Rent(options.ReadBufferBytes);
        var frameBuffer = ArrayPool<byte>.Shared.Rent(Math.Min(options.InitialFrameBufferBytes, options.MaxInputFrameBytes));

        return new StdioMcpTransportSession(
            input,
            output,
            readBuffer,
            frameBuffer,
            options.ClearReturnedInputBuffers,
            options.AllowFinalFrameWithoutNewline);
    }

    public async ValueTask<StdioReadResult> ReadFrameAsync(int maxFrameBytes, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameBytes);

        while (true)
        {
            var readBuffer = _readBuffer ?? throw new ObjectDisposedException(nameof(StdioMcpTransportSession));

            if (_readOffset < _readCount)
            {
                var pending = readBuffer.AsSpan(_readOffset, _readCount - _readOffset);
                var newLineIndex = pending.IndexOf((byte)'\n');

                if (newLineIndex >= 0)
                {
                    var segment = pending[..newLineIndex];
                    _readOffset += newLineIndex + 1;

                    if (_discardingOversizedFrame || !AppendToFrame(segment, maxFrameBytes, trimFinalCarriageReturn: true))
                    {
                        ResetFrame();
                        _discardingOversizedFrame = false;
                        return StdioReadResult.TooLarge();
                    }

                    TrimFinalCarriageReturnFromFrame();
                    if (FrameContainsCarriageReturn())
                    {
                        ResetFrame();
                        return StdioReadResult.InvalidFrame();
                    }

                    var frame = TakeFrame();
                    return StdioReadResult.Success(frame);
                }

                _readOffset = _readCount;

                if (_discardingOversizedFrame)
                {
                    continue;
                }

                if (!AppendToFrame(pending, maxFrameBytes, trimFinalCarriageReturn: false))
                {
                    ResetFrame();
                    _discardingOversizedFrame = true;
                }

                continue;
            }

            _readOffset = 0;
            _readCount = 0;

            var read = await _input.ReadAsync(readBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (_discardingOversizedFrame)
                {
                    ResetFrame();
                    _discardingOversizedFrame = false;
                    return StdioReadResult.TooLarge();
                }

                if (_frameLength > 0)
                {
                    if (!_allowFinalFrameWithoutNewline)
                    {
                        ResetFrame();
                        return StdioReadResult.InvalidFrame();
                    }

                    TrimFinalCarriageReturnFromFrame();
                    if (FrameContainsCarriageReturn())
                    {
                        ResetFrame();
                        return StdioReadResult.InvalidFrame();
                    }

                    var frame = TakeFrame();
                    return StdioReadResult.Success(frame);
                }

                return StdioReadResult.EndOfInput();
            }

            _readCount = read;
        }
    }

    private bool AppendToFrame(ReadOnlySpan<byte> source, int maxFrameBytes, bool trimFinalCarriageReturn)
    {
        if (source.Length == 0)
        {
            return true;
        }

        if (trimFinalCarriageReturn && source[^1] == (byte)'\r')
        {
            source = source[..^1];
            if (source.Length == 0)
            {
                return true;
            }
        }

        var newLength = _frameLength + source.Length;
        if (newLength > maxFrameBytes)
        {
            return false;
        }

        EnsureFrameCapacity(newLength);
        var frameBuffer = _frameBuffer ?? throw new ObjectDisposedException(nameof(StdioMcpTransportSession));
        source.CopyTo(frameBuffer.AsSpan(_frameLength));
        _frameLength = newLength;
        return true;
    }

    private void EnsureFrameCapacity(int requiredLength)
    {
        var frameBuffer = _frameBuffer ?? throw new ObjectDisposedException(nameof(StdioMcpTransportSession));
        if (requiredLength <= frameBuffer.Length)
        {
            return;
        }

        var nextLength = frameBuffer.Length;
        while (nextLength < requiredLength)
        {
            nextLength = checked(nextLength * 2);
        }

        var replacement = ArrayPool<byte>.Shared.Rent(nextLength);
        frameBuffer.AsSpan(0, _frameLength).CopyTo(replacement);
        ArrayPool<byte>.Shared.Return(frameBuffer, _clearReturnedInputBuffers);
        _frameBuffer = replacement;
    }

    private void TrimFinalCarriageReturnFromFrame()
    {
        if (_frameLength == 0)
        {
            return;
        }

        var frameBuffer = _frameBuffer ?? throw new ObjectDisposedException(nameof(StdioMcpTransportSession));
        if (frameBuffer[_frameLength - 1] == (byte)'\r')
        {
            _frameLength--;
        }
    }

    private bool FrameContainsCarriageReturn()
    {
        if (_frameLength == 0)
        {
            return false;
        }

        var frameBuffer = _frameBuffer ?? throw new ObjectDisposedException(nameof(StdioMcpTransportSession));
        return frameBuffer.AsSpan(0, _frameLength).IndexOf((byte)'\r') >= 0;
    }

    private PooledByteBuffer TakeFrame()
    {
        var frameBuffer = _frameBuffer ?? throw new ObjectDisposedException(nameof(StdioMcpTransportSession));
        var frameLength = _frameLength;

        _frameBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, Math.Min(4 * 1024, frameBuffer.Length)));
        _frameLength = 0;

        return PooledByteBuffer.Own(frameBuffer, frameLength, _clearReturnedInputBuffers);
    }

    private void ResetFrame()
    {
        var frameBuffer = _frameBuffer;
        if (frameBuffer is null)
        {
            return;
        }

        if (_clearReturnedInputBuffers && _frameLength > 0)
        {
            frameBuffer.AsSpan(0, _frameLength).Clear();
        }

        _frameLength = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReturnBuffers();
        _input.Dispose();
        _output.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        ReturnBuffers();
        await _input.DisposeAsync().ConfigureAwait(false);
        await _output.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ReturnBuffers()
    {
        var readBuffer = _readBuffer;
        if (readBuffer is not null)
        {
            _readBuffer = default;
            ArrayPool<byte>.Shared.Return(readBuffer, _clearReturnedInputBuffers);
        }

        var frameBuffer = _frameBuffer;
        if (frameBuffer is not null)
        {
            _frameBuffer = default;
            ArrayPool<byte>.Shared.Return(frameBuffer, _clearReturnedInputBuffers);
        }

        _frameLength = 0;
        _readOffset = 0;
        _readCount = 0;
    }
}
