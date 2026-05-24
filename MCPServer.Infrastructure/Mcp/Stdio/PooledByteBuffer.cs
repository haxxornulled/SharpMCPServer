using System.Buffers;

namespace MCPServer.Infrastructure.Mcp.Stdio;

public sealed class PooledByteBuffer : IDisposable
{
    private readonly bool _clearOnDispose;
    private byte[]? _buffer;

    private PooledByteBuffer(byte[] buffer, int length, bool clearOnDispose)
    {
        _buffer = buffer;
        Length = length;
        _clearOnDispose = clearOnDispose;
    }

    public int Length { get; }

    public ReadOnlyMemory<byte> Memory
    {
        get
        {
            var buffer = _buffer;
            ObjectDisposedException.ThrowIf(buffer is null, this);
            return buffer.AsMemory(0, Length);
        }
    }

    internal static PooledByteBuffer Own(byte[] buffer, int length, bool clearOnDispose)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length cannot exceed the rented buffer length.");
        }

        return new PooledByteBuffer(buffer, length, clearOnDispose);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null)
        {
            return;
        }

        _buffer = default;
        ArrayPool<byte>.Shared.Return(buffer, _clearOnDispose);
    }
}
