using System.Buffers;

namespace MCPServer.Infrastructure.Mcp.JsonRpc;

internal sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int DefaultInitialCapacity = 512;
    private readonly bool _clearOnDispose;
    private byte[]? _buffer;
    private int _index;

    private PooledByteBufferWriter(byte[] buffer, bool clearOnDispose)
    {
        _buffer = buffer;
        _clearOnDispose = clearOnDispose;
    }

    public ReadOnlyMemory<byte> WrittenMemory
    {
        get
        {
            var buffer = _buffer;
            ObjectDisposedException.ThrowIf(buffer is null, this);
            return buffer.AsMemory(0, _index);
        }
    }

    public int WrittenCount => _index;

    public int Capacity
    {
        get
        {
            var buffer = _buffer;
            ObjectDisposedException.ThrowIf(buffer is null, this);
            return buffer.Length;
        }
    }

    public int FreeCapacity => Capacity - _index;

    public static PooledByteBufferWriter Rent(int initialCapacity = DefaultInitialCapacity, bool clearOnDispose = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        return new PooledByteBufferWriter(ArrayPool<byte>.Shared.Rent(initialCapacity), clearOnDispose);
    }

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count > FreeCapacity)
        {
            throw new InvalidOperationException("Cannot advance past the end of the rented buffer.");
        }

        _index += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledByteBufferWriter));
        return buffer.AsMemory(_index);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledByteBufferWriter));
        return buffer.AsSpan(_index);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null)
        {
            return;
        }

        _buffer = default;
        _index = 0;
        ArrayPool<byte>.Shared.Return(buffer, _clearOnDispose);
    }

    private void CheckAndResizeBuffer(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint <= FreeCapacity)
        {
            return;
        }

        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledByteBufferWriter));
        var growBy = Math.Max(sizeHint, buffer.Length);
        var newSize = checked(buffer.Length + growBy);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);

        buffer.AsSpan(0, _index).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(buffer, _clearOnDispose);
        _buffer = newBuffer;
    }
}
