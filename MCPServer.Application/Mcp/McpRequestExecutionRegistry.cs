using System.Collections.Concurrent;
using System.Text.Json;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using Microsoft.Extensions.Logging;

namespace MCPServer.Application.Mcp;

public sealed class McpRequestExecutionRegistry : IMcpRequestExecutionRegistry, IDisposable
{
    private readonly ConcurrentDictionary<string, ActiveRequest> _activeRequests;
    private readonly ConcurrentDictionary<string, string> _activeProgressTokens;
    private readonly McpRequestExecutionOptions _options;
    private readonly ILogger<McpRequestExecutionRegistry> _logger;
    private bool _disposed;

    public McpRequestExecutionRegistry(McpRequestExecutionOptions options, ILogger<McpRequestExecutionRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
        _activeRequests = new ConcurrentDictionary<string, ActiveRequest>(StringComparer.Ordinal);
        _activeProgressTokens = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
    }

    public Fin<McpRequestExecutionScope> Register(JsonRpcMessage message, CancellationToken transportCancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!message.HasId || message.Method is McpMethods.Initialize)
        {
            return Fin.Succ<McpRequestExecutionScope>(new McpRequestExecutionScope(transportCancellationToken, registration: default));
        }

        if (!message.Id.TryGetStableKey(out var requestKey))
        {
            return Fin.Succ<McpRequestExecutionScope>(new McpRequestExecutionScope(transportCancellationToken, registration: default));
        }

        var progressTokenResult = TryGetProgressToken(message.Params);
        var progressTokenOutcome = progressTokenResult.Match(
            Succ: static token => ProgressTokenOutcome.Success(token),
            Fail: static error => ProgressTokenOutcome.Fail(error));

        if (progressTokenOutcome is not { IsSuccess: true })
        {
            return Fin.Fail<McpRequestExecutionScope>(progressTokenOutcome.Error ?? Error.New("MCP progressToken validation failed."));
        }

        string? progressTokenKey = progressTokenOutcome.Token is { IsSpecified: true } && progressTokenOutcome.Token.TryGetStableKey(out var tokenKey)
            ? tokenKey
            : default;

        if (progressTokenKey is { } activeProgressToken && !_activeProgressTokens.TryAdd(activeProgressToken, requestKey))
        {
            return Fin.Fail<McpRequestExecutionScope>(Error.New("MCP progressToken must be unique across active requests."));
        }

        var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(transportCancellationToken);
        var method = message.Method is { } requestMethod ? requestMethod : string.Empty;
        ApplyTimeout(linkedSource, method);
        var activeRequest = new ActiveRequest(method, linkedSource, progressTokenKey);

        if (!_activeRequests.TryAdd(requestKey, activeRequest))
        {
            linkedSource.Dispose();
            if (progressTokenKey is not null)
            {
                _activeProgressTokens.TryRemove(progressTokenKey, out _);
            }

            _logger.LogWarning("Duplicate active MCP request id {RequestId}; request will use transport cancellation only", requestKey);
            return Fin.Succ<McpRequestExecutionScope>(new McpRequestExecutionScope(transportCancellationToken, registration: default));
        }

        return Fin.Succ<McpRequestExecutionScope>(new McpRequestExecutionScope(activeRequest.CancellationToken, new ActiveRequestRegistration(this, requestKey, activeRequest)));
    }

    public bool TryCancel(JsonRpcRequestId requestId, string? reason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!requestId.TryGetStableKey(out var key))
        {
            return false;
        }

        if (!_activeRequests.TryGetValue(key, out var activeRequest))
        {
            return false;
        }

        _logger.LogInformation(
            "Cancellation requested for MCP request {RequestId} ({Method}). Reason: {Reason}",
            key,
            activeRequest.Method,
            string.IsNullOrWhiteSpace(reason) ? "<none>" : reason);

        activeRequest.Cancel();
        return true;
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var pair in _activeRequests)
        {
            if (_activeRequests.TryRemove(pair.Key, out var activeRequest))
            {
                if (activeRequest.ProgressTokenKey is not null)
                {
                    _activeProgressTokens.TryRemove(activeRequest.ProgressTokenKey, out _);
                }

                activeRequest.Cancel();
                activeRequest.Dispose();
            }
        }

        _activeProgressTokens.Clear();
    }

    private static Fin<McpProgressToken> TryGetProgressToken(JsonElement? parameters)
    {
        if (parameters is not { } suppliedParameters || suppliedParameters is not { ValueKind: JsonValueKind.Object })
        {
            return Fin.Succ<McpProgressToken>(McpProgressToken.Missing);
        }

        if (!suppliedParameters.TryGetProperty("_meta"u8, out var meta))
        {
            return Fin.Succ<McpProgressToken>(McpProgressToken.Missing);
        }

        if (meta is { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null })
        {
            return Fin.Succ<McpProgressToken>(McpProgressToken.Missing);
        }

        if (!McpMetaKeyValidator.TryValidateObjectKeys(meta, out var metaError))
        {
            return Fin.Fail<McpProgressToken>(Error.New(metaError));
        }

        if (!meta.TryGetProperty("progressToken"u8, out var tokenElement))
        {
            return Fin.Succ<McpProgressToken>(McpProgressToken.Missing);
        }

        if (!McpProgressToken.TryFromElement(tokenElement, out var token))
        {
            return Fin.Fail<McpProgressToken>(Error.New("MCP progressToken must be a string or integer."));
        }

        return Fin.Succ<McpProgressToken>(token);
    }

    private void ApplyTimeout(CancellationTokenSource cancellationTokenSource, string? method)
    {
        if (string.Equals(method, McpMethods.Ping, StringComparison.Ordinal))
        {
            return;
        }

        var timeout = _options.DefaultRequestTimeout;
        if (timeout is not { } timeoutValue || timeoutValue <= TimeSpan.Zero)
        {
            return;
        }

        cancellationTokenSource.CancelAfter(timeoutValue);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var pair in _activeRequests)
        {
            if (_activeRequests.TryRemove(pair.Key, out var activeRequest))
            {
                if (activeRequest.ProgressTokenKey is not null)
                {
                    _activeProgressTokens.TryRemove(activeRequest.ProgressTokenKey, out _);
                }

                activeRequest.Dispose();
            }
        }

        _activeProgressTokens.Clear();
    }

    private void Complete(string key, ActiveRequest activeRequest)
    {
        if (!_activeRequests.TryRemove(key, out var removed))
        {
            return;
        }

        if (!ReferenceEquals(removed, activeRequest))
        {
            _activeRequests.TryAdd(key, removed);
            return;
        }

        if (activeRequest.ProgressTokenKey is not null)
        {
            _activeProgressTokens.TryRemove(activeRequest.ProgressTokenKey, out _);
        }

        activeRequest.Dispose();
    }

    private sealed class ActiveRequest : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource;

        public ActiveRequest(string method, CancellationTokenSource cancellationTokenSource, string? progressTokenKey)
        {
            Method = method;
            _cancellationTokenSource = cancellationTokenSource;
            ProgressTokenKey = progressTokenKey;
        }

        public string Method { get; }

        public string? ProgressTokenKey { get; }

        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public void Cancel()
        {
            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Dispose();
        }
    }

    private sealed class ActiveRequestRegistration : IDisposable
    {
        private McpRequestExecutionRegistry? _owner;
        private ActiveRequest? _activeRequest;
        private readonly string _key;

        public ActiveRequestRegistration(McpRequestExecutionRegistry owner, string key, ActiveRequest activeRequest)
        {
            _owner = owner;
            _key = key;
            _activeRequest = activeRequest;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, default);
            var activeRequest = Interlocked.Exchange(ref _activeRequest, default);
            if ((owner, activeRequest) is not ({ } activeOwner, { } completedRequest))
            {
                return;
            }

            activeOwner.Complete(_key, completedRequest);
        }
    }

    private readonly struct ProgressTokenOutcome
    {
        private ProgressTokenOutcome(McpProgressToken token, Error? error, bool isSuccess)
        {
            Token = token;
            Error = error;
            IsSuccess = isSuccess;
        }

        public McpProgressToken Token { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static ProgressTokenOutcome Success(McpProgressToken token)
        {
            return new ProgressTokenOutcome(token, default, isSuccess: true);
        }

        public static ProgressTokenOutcome Fail(Error error)
        {
            return new ProgressTokenOutcome(default, error, isSuccess: false);
        }
    }
}
