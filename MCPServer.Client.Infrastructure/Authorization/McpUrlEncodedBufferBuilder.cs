using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MCPServer.Client.Infrastructure.Authorization;

internal sealed class McpUrlEncodedBufferBuilder : IDisposable
{
    private byte[]? _buffer;
    private int _position;
    private bool _needsSeparator;

    public McpUrlEncodedBufferBuilder(int initialCapacity = 256)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    public void AppendField(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        EnsureCapacity(GetEncodedLength(name) + GetEncodedLength(value) + 2);

        if (_needsSeparator)
        {
            _buffer![_position++] = (byte)'&';
        }

        AppendEncoded(name);
        _buffer![_position++] = (byte)'=';
        AppendEncoded(value);
        _needsSeparator = true;
    }

    public string ToAsciiString()
    {
        var buffer = DetachBuffer(out var length);
        try
        {
            return Encoding.ASCII.GetString(buffer, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    public McpPooledFormUrlEncodedContent ToContent()
    {
        return new McpPooledFormUrlEncodedContent(DetachBuffer(out var length), length);
    }

    public void Dispose()
    {
        if (_buffer is { } buffer)
        {
            _buffer = null;
            _position = 0;
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private byte[] DetachBuffer(out int length)
    {
        if (_buffer is not { } buffer)
        {
            throw new ObjectDisposedException(nameof(McpUrlEncodedBufferBuilder));
        }

        _buffer = null;
        length = _position;
        _position = 0;
        return buffer;
    }

    private void AppendEncoded(string value)
    {
        Span<byte> utf8 = stackalloc byte[4];
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value == ' ')
            {
                EnsureCapacity(1);
                _buffer![_position++] = (byte)'+';
                continue;
            }

            if (rune.IsAscii && IsUnreserved((char)rune.Value))
            {
                EnsureCapacity(1);
                _buffer![_position++] = (byte)rune.Value;
                continue;
            }

            if (!rune.TryEncodeToUtf8(utf8, out var bytesWritten))
            {
                throw new InvalidOperationException("Failed to encode a Unicode rune to UTF-8.");
            }

            EnsureCapacity(bytesWritten * 3);
            for (var i = 0; i < bytesWritten; i++)
            {
                AppendPercentEncoded(utf8[i]);
            }
        }
    }

    private void AppendPercentEncoded(byte value)
    {
        EnsureCapacity(3);
        const string Hex = "0123456789ABCDEF";
        _buffer![_position++] = (byte)'%';
        _buffer![_position++] = (byte)Hex[value >> 4];
        _buffer![_position++] = (byte)Hex[value & 0x0F];
    }

    private void EnsureCapacity(int additionalBytes)
    {
        if (_buffer is null)
        {
            throw new ObjectDisposedException(nameof(McpUrlEncodedBufferBuilder));
        }

        if (_position + additionalBytes <= _buffer.Length)
        {
            return;
        }

        var required = _position + additionalBytes;
        var rented = ArrayPool<byte>.Shared.Rent(Math.Max(required, _buffer.Length * 2));
        _buffer.AsSpan(0, _position).CopyTo(rented);
        ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
        _buffer = rented;
    }

    private static int GetEncodedLength(string value)
    {
        var length = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value == ' ' || (rune.IsAscii && IsUnreserved((char)rune.Value)))
            {
                length += 1;
                continue;
            }

            length += rune.Utf8SequenceLength * 3;
        }

        return length;
    }

    private static bool IsUnreserved(char value)
    {
        return value is >= 'A' and <= 'Z'
               or >= 'a' and <= 'z'
               or >= '0' and <= '9'
               or '-'
               or '.'
               or '_'
               or '~';
    }
}

internal sealed class McpPooledFormUrlEncodedContent : HttpContent
{
    private byte[]? _buffer;
    private readonly int _length;

    internal McpPooledFormUrlEncodedContent(byte[] buffer, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
        _length = length;
        Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        if (_buffer is not { } buffer)
        {
            throw new ObjectDisposedException(nameof(McpPooledFormUrlEncodedContent));
        }

        return stream.WriteAsync(buffer.AsMemory(0, _length)).AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _buffer is { } buffer)
        {
            _buffer = null;
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        base.Dispose(disposing);
    }
}

internal static class McpPkceUtilities
{
    public static string CreateState()
    {
        return CreateBase64UrlString(32);
    }

    public static string CreateCodeVerifier()
    {
        return CreateBase64UrlString(32);
    }

    public static string CreateCodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);

        Span<byte> utf8 = stackalloc byte[128];
        var bytesWritten = Encoding.ASCII.GetBytes(codeVerifier.AsSpan(), utf8);
        if (bytesWritten <= 0)
        {
            throw new InvalidOperationException("The PKCE verifier was too long to encode.");
        }

        Span<byte> hash = stackalloc byte[32];
        if (!System.Security.Cryptography.SHA256.TryHashData(utf8[..bytesWritten], hash, out var hashBytesWritten) || hashBytesWritten != 32)
        {
            throw new InvalidOperationException("Failed to compute the PKCE code challenge.");
        }

        return ToBase64UrlString(hash[..hashBytesWritten]);
    }

    private static string CreateBase64UrlString(int byteCount)
    {
        Span<byte> bytes = stackalloc byte[byteCount];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return ToBase64UrlString(bytes);
    }

    private static string ToBase64UrlString(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = stackalloc char[((bytes.Length + 2) / 3) * 4];
        if (!Convert.TryToBase64Chars(bytes, chars, out var charsWritten))
        {
            throw new InvalidOperationException("Failed to encode base64url data.");
        }

        var length = charsWritten;
        for (var i = 0; i < length; i++)
        {
            chars[i] = chars[i] switch
            {
                '+' => '-',
                '/' => '_',
                _ => chars[i]
            };
        }

        while (length > 0 && chars[length - 1] == '=')
        {
            length--;
        }

        return new string(chars[..length]);
    }
}
