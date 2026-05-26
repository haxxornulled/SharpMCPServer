using System.Buffers;

namespace MCPServer.AgentRouter.PythonBridge.Native;

internal sealed class PooledUtf8BufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;
    private bool _disposed;

    public PooledUtf8BufferWriter(int initialCapacity = 256)
    {
        if (initialCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    public int WrittenCount => _written;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public void Advance(int count)
    {
        ThrowIfDisposed();

        if (count < 0 || _written + count > _buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ThrowIfDisposed();
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ThrowIfDisposed();
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = Array.Empty<byte>();
        _written = 0;
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        var requiredSize = sizeHint <= 0 ? 1 : sizeHint;
        var available = _buffer.Length - _written;
        if (available >= requiredSize)
        {
            return;
        }

        var minimumSize = Math.Max(requiredSize, 256);
        var newCapacity = Math.Max(_buffer.Length * 2, _written + minimumSize);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _written);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
