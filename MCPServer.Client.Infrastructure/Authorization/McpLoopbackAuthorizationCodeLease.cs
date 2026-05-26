using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LanguageExt;
using LanguageExt.Common;

namespace MCPServer.Client.Infrastructure.Authorization;

internal sealed class McpLoopbackAuthorizationCodeLease : IAsyncDisposable
{
    private static readonly byte[] SuccessResponseBytes = Encoding.UTF8.GetBytes(
        "HTTP/1.1 200 OK\r\n" +
        "Content-Type: text/html; charset=utf-8\r\n" +
        "Cache-Control: no-store\r\n" +
        "Pragma: no-cache\r\n" +
        "Connection: close\r\n" +
        "\r\n" +
        "<html><body>Authorization complete. You may close this window.</body></html>");

    private static readonly byte[] FailureResponseBytes = Encoding.UTF8.GetBytes(
        "HTTP/1.1 400 Bad Request\r\n" +
        "Content-Type: text/html; charset=utf-8\r\n" +
        "Cache-Control: no-store\r\n" +
        "Pragma: no-cache\r\n" +
        "Connection: close\r\n" +
        "\r\n" +
        "<html><body>Authorization failed. You may close this window.</body></html>");

    private static readonly byte[] NotFoundResponseBytes = Encoding.UTF8.GetBytes(
        "HTTP/1.1 404 Not Found\r\n" +
        "Content-Type: text/html; charset=utf-8\r\n" +
        "Cache-Control: no-store\r\n" +
        "Pragma: no-cache\r\n" +
        "Connection: close\r\n" +
        "\r\n" +
        "<html><body>Not found.</body></html>");

    private const int MaxRequestBytes = 8192;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdownCts;
    private Task? _acceptLoop;
    private readonly TaskCompletionSource<Fin<McpLoopbackAuthorizationCodeResult>> _result;
    private readonly string _expectedState;
    private readonly byte[] _expectedPathBytes;
    private readonly Uri _redirectUri;
    private bool _disposed;

    private McpLoopbackAuthorizationCodeLease(
        TcpListener listener,
        CancellationTokenSource shutdownCts,
        TaskCompletionSource<Fin<McpLoopbackAuthorizationCodeResult>> result,
        Uri redirectUri,
        string expectedState,
        string callbackPath)
    {
        _listener = listener;
        _shutdownCts = shutdownCts;
        _result = result;
        _redirectUri = redirectUri;
        _expectedState = expectedState;
        _expectedPathBytes = Encoding.ASCII.GetBytes(NormalizeCallbackPath(callbackPath));
    }

    public Uri RedirectUri => _redirectUri;

    public static async ValueTask<Fin<McpLoopbackAuthorizationCodeLease>> StartAsync(
        string expectedState,
        Uri? fixedRedirectUri,
        string callbackPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackPath);

        TcpListener? listener = null;
        Uri? redirectUri = null;
        var normalizedPath = NormalizeCallbackPath(callbackPath);

        try
        {
            if (fixedRedirectUri is not null)
            {
                if (!fixedRedirectUri.IsAbsoluteUri ||
                    !string.Equals(fixedRedirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    !fixedRedirectUri.IsLoopback ||
                    !string.IsNullOrWhiteSpace(fixedRedirectUri.Query) ||
                    !string.IsNullOrWhiteSpace(fixedRedirectUri.Fragment))
                {
                    return Fin.Fail<McpLoopbackAuthorizationCodeLease>(Error.New("The configured redirect URI must use a loopback HTTP URI without query or fragment components."));
                }

                listener = new TcpListener(IPAddress.Loopback, fixedRedirectUri.Port);
                redirectUri = fixedRedirectUri;
                normalizedPath = NormalizeCallbackPath(fixedRedirectUri.AbsolutePath);
            }
            else
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                redirectUri = new Uri($"http://127.0.0.1:{port}{normalizedPath}", UriKind.Absolute);
            }

            if (fixedRedirectUri is not null)
            {
                listener.Start();
            }

            var shutdownCts = new CancellationTokenSource();
            var result = new TaskCompletionSource<Fin<McpLoopbackAuthorizationCodeResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (listener is null || redirectUri is null)
            {
                throw new InvalidOperationException("The loopback authorization listener failed to initialize.");
            }

            var lease = new McpLoopbackAuthorizationCodeLease(listener, shutdownCts, result, redirectUri, expectedState, normalizedPath);
            lease._acceptLoop = lease.RunAcceptLoopAsync(cancellationToken);
            return Fin.Succ(lease);
        }
        catch (Exception ex)
        {
            listener?.Stop();
            listener?.Dispose();
            return Fin.Fail<McpLoopbackAuthorizationCodeLease>(Error.New(ex.Message));
        }
    }

    public async ValueTask<Fin<McpLoopbackAuthorizationCodeResult>> WaitForResultAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New("The loopback authorization lease has already been disposed."));
        }

        try
        {
            return await _result.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New("The authorization code callback timed out."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _shutdownCts.Cancel();
        }
        catch
        {
        }

        try
        {
            _listener.Stop();
        }
        catch
        {
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _shutdownCts.Dispose();
        _listener.Dispose();
    }

    private async Task RunAcceptLoopAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);

        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(linkedCts.Token).ConfigureAwait(false);
                _ = HandleClientAsync(client, linkedCts.Token);

                if (_result.Task.IsCompleted)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (linkedCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _result.TrySetResult(Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New($"The loopback redirect listener failed: {ex.Message}")));
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var clientHandle = client;

        try
        {
            using var stream = client.GetStream();
            using var bufferLease = new PooledBufferLease(MaxRequestBytes);

            var bytesRead = await ReadHeaderBlockAsync(stream, bufferLease.Buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return;
            }

            var parseDisposition = TryParseCallback(bufferLease.Buffer.AsSpan(0, bytesRead), out var callbackResult);
            switch (parseDisposition)
            {
                case CallbackParseDisposition.Ignore:
                    await stream.WriteAsync(NotFoundResponseBytes, cancellationToken).ConfigureAwait(false);
                    return;
                case CallbackParseDisposition.Failure:
                    _result.TrySetResult(callbackResult);
                    await stream.WriteAsync(FailureResponseBytes, cancellationToken).ConfigureAwait(false);
                    return;
                case CallbackParseDisposition.Success:
                    _result.TrySetResult(callbackResult);
                    await stream.WriteAsync(SuccessResponseBytes, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new InvalidOperationException($"Unexpected callback parse disposition '{parseDisposition}'.");
            }
        }
        catch (Exception ex)
        {
            _result.TrySetResult(Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New($"The authorization callback failed: {ex.Message}")));
            try
            {
                using var stream = client.GetStream();
                await stream.WriteAsync(FailureResponseBytes, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private async ValueTask<int> ReadHeaderBlockAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer.Slice(total), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return total;
            }

            total += bytesRead;
            if (FindHeaderTerminator(buffer.Span[..total]) >= 0)
            {
                return total;
            }

            if (total == buffer.Length)
            {
                throw new InvalidOperationException("The authorization callback request was too large.");
            }
        }
    }

    private CallbackParseDisposition TryParseCallback(ReadOnlySpan<byte> requestBytes, out Fin<McpLoopbackAuthorizationCodeResult> callbackResult)
    {
        callbackResult = Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New("The authorization callback was not recognized."));

        var firstLineEnd = FindCrlf(requestBytes);
        if (firstLineEnd < 0)
        {
            callbackResult = Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New("The authorization callback request did not include a valid request line."));
            return CallbackParseDisposition.Failure;
        }

        var firstLine = requestBytes[..firstLineEnd];
        if (!firstLine.StartsWith("GET"u8))
        {
            callbackResult = Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New("The authorization callback must use HTTP GET."));
            return CallbackParseDisposition.Failure;
        }

        var firstSpace = firstLine.IndexOf((byte)' ');
        if (firstSpace < 0)
        {
            callbackResult = Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New("The authorization callback request line was malformed."));
            return CallbackParseDisposition.Failure;
        }

        var secondSpace = firstLine[(firstSpace + 1)..].IndexOf((byte)' ');
        if (secondSpace < 0)
        {
            callbackResult = Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New("The authorization callback request line was malformed."));
            return CallbackParseDisposition.Failure;
        }

        var target = firstLine[(firstSpace + 1)..(firstSpace + 1 + secondSpace)];
        var queryIndex = target.IndexOf((byte)'?');
        var path = queryIndex >= 0 ? target[..queryIndex] : target;
        if (!path.SequenceEqual(_expectedPathBytes))
        {
            return CallbackParseDisposition.Ignore;
        }

        var query = queryIndex >= 0 ? target[(queryIndex + 1)..] : ReadOnlySpan<byte>.Empty;
        var error = TryReadQueryParameter(query, "error"u8);
        if (!string.IsNullOrWhiteSpace(error))
        {
            var errorDescription = TryReadQueryParameter(query, "error_description"u8);
            var message = string.IsNullOrWhiteSpace(errorDescription)
                ? $"The authorization server returned an error: {error}"
                : $"The authorization server returned an error: {error} ({errorDescription})";
            callbackResult = Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New(message));
            return CallbackParseDisposition.Failure;
        }

        var code = TryReadQueryParameter(query, "code"u8);
        var state = TryReadQueryParameter(query, "state"u8);
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            callbackResult = Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New("The authorization callback did not include both code and state."));
            return CallbackParseDisposition.Failure;
        }

        if (!string.Equals(state, _expectedState, StringComparison.Ordinal))
        {
            callbackResult = Fin.Fail<McpLoopbackAuthorizationCodeResult>(Error.New("The authorization callback state did not match."));
            return CallbackParseDisposition.Failure;
        }

        callbackResult = Fin.Succ(new McpLoopbackAuthorizationCodeResult(code, state));
        return CallbackParseDisposition.Success;
    }

    private static int FindHeaderTerminator(ReadOnlySpan<byte> buffer)
    {
        for (var index = 0; index + 3 < buffer.Length; index++)
        {
            if (buffer[index] == (byte)'\r' &&
                buffer[index + 1] == (byte)'\n' &&
                buffer[index + 2] == (byte)'\r' &&
                buffer[index + 3] == (byte)'\n')
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindCrlf(ReadOnlySpan<byte> buffer)
    {
        for (var index = 0; index + 1 < buffer.Length; index++)
        {
            if (buffer[index] == (byte)'\r' && buffer[index + 1] == (byte)'\n')
            {
                return index;
            }
        }

        return -1;
    }

    private static string? TryReadQueryParameter(ReadOnlySpan<byte> query, ReadOnlySpan<byte> name)
    {
        while (!query.IsEmpty)
        {
            var ampIndex = query.IndexOf((byte)'&');
            var pair = ampIndex >= 0 ? query[..ampIndex] : query;
            var equalsIndex = pair.IndexOf((byte)'=');
            if (equalsIndex > 0)
            {
                var parameterName = pair[..equalsIndex];
                if (parameterName.SequenceEqual(name))
                {
                    var rawValue = Encoding.ASCII.GetString(pair[(equalsIndex + 1)..]);
                    return Uri.UnescapeDataString(rawValue);
                }
            }

            if (ampIndex < 0)
            {
                break;
            }

            query = query[(ampIndex + 1)..];
        }

        return null;
    }

    private static string NormalizeCallbackPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.StartsWith('/') ? path : "/" + path;
    }

    private enum CallbackParseDisposition
    {
        Ignore,
        Failure,
        Success
    }

    private sealed class PooledBufferLease : IDisposable
    {
        private byte[]? _buffer;

        public PooledBufferLease(int minimumLength)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(minimumLength);
        }

        public byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferLease));

        public void Dispose()
        {
            if (_buffer is { } buffer)
            {
                _buffer = null;
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }
}

internal readonly struct McpLoopbackAuthorizationCodeResult
{
    public McpLoopbackAuthorizationCodeResult(string code, string state)
    {
        Code = code;
        State = state;
    }

    public string Code { get; }

    public string State { get; }
}
