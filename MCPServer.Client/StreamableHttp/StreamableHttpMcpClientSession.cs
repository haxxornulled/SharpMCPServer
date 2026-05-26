using System.Net;
using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Client.Authorization;
using MCPServer.Client.Interfaces;
using MCPServer.Client.Internal;
using MCPServer.Client.Tasking;
using MCPServer.Domain.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPServer.Client.StreamableHttp;

public sealed class StreamableHttpMcpClientSession : IMcpClientSession
{
    private readonly McpStreamableHttpClientOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<StreamableHttpMcpClientSession> _logger;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim _sessionRecoveryLock = new SemaphoreSlim(1, 1);
    private readonly CancellationTokenSource _listenCts = new CancellationTokenSource();
    private readonly McpClientInboundMessageRouter _inboundRouter;
    private readonly ConcurrentDictionary<string, McpToolDescriptor> _knownTools = new ConcurrentDictionary<string, McpToolDescriptor>(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, McpToolHeaderBinding[]> _toolHeaderBindings = new ConcurrentDictionary<string, McpToolHeaderBinding[]>(StringComparer.Ordinal);
    private Task? _serverEventPump;
    private string? _sessionId;
    private string? _accessToken;
    private string? _lastEventId;
    private string? _negotiatedProtocolVersion;
    private TimeSpan _reconnectDelay;
    private int _nextRequestId;
    private bool _disposed;

    private StreamableHttpMcpClientSession(McpStreamableHttpClientOptions options, HttpClient httpClient, ILogger<StreamableHttpMcpClientSession> logger)
    {
        _options = options;
        _httpClient = httpClient;
        _logger = logger;
        _reconnectDelay = options.ServerEventReconnectDelay;

        var taskRegistry = options.ClientTaskRegistry ?? new InMemoryMcpClientTaskRegistry();
        _inboundRouter = new McpClientInboundMessageRouter(
            options.SamplingRequestHandler,
            options.ElicitationRequestHandler,
            taskRegistry,
            options.TaskStatusObserver,
            SendResultResponseAsync,
            SendErrorResponseAsync,
            SendNotificationAsync,
            logger);
    }

    public static ValueTask<Fin<StreamableHttpMcpClientSession>> StartAsync(
        McpStreamableHttpClientOptions options,
        ILogger<StreamableHttpMcpClientSession>? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        HttpClient httpClient;
        if (options.HttpClientFactory is not null)
        {
            var httpClientName = string.IsNullOrWhiteSpace(options.HttpClientName)
                ? nameof(StreamableHttpMcpClientSession)
                : options.HttpClientName;
            httpClient = options.HttpClientFactory.CreateClient(httpClientName);
        }
        else if (options.HttpMessageHandler is not null)
        {
            httpClient = new HttpClient(options.HttpMessageHandler, disposeHandler: false);
        }
        else
        {
            return new ValueTask<Fin<StreamableHttpMcpClientSession>>(
                Fin.Fail<StreamableHttpMcpClientSession>(Error.New("Streamable HTTP client requires either an IHttpClientFactory or an explicit HttpMessageHandler.")));
        }

        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        return new ValueTask<Fin<StreamableHttpMcpClientSession>>(Fin.Succ(new StreamableHttpMcpClientSession(options, httpClient, logger ?? NullLogger<StreamableHttpMcpClientSession>.Instance)));
    }

    public async ValueTask<Fin<InitializeResult>> InitializeAsync(CancellationToken cancellationToken)
    {
        var initializeResult = await SendInitializeRequestAsync(cancellationToken, acquireSendLock: true).ConfigureAwait(false);
        return await FinalizeInitializationAsync(initializeResult, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Fin<ToolsListResult>> ListToolsAsync(string? cursor, CancellationToken cancellationToken)
    {
        var id = NextRequestId();
        var result = await SendRequestAsync(id, McpMethods.ToolsList, writer =>
        {
            writer.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                writer.WriteString("cursor", cursor);
            }
            writer.WriteEndObject();
        }, cancellationToken).ConfigureAwait(false);

        return result.Match(
            Succ: value =>
            {
                var toolsResult = Deserialize(value, McpJsonSerializerContext.Default.ToolsListResult);
                if (toolsResult.IsFail)
                {
                    return toolsResult;
                }

                var filtered = FilterAndCacheTools(toolsResult.Match(Succ: static tools => tools, Fail: static _ => throw new InvalidOperationException()));
                return Fin.Succ(filtered);
            },
            Fail: error => Fin.Fail<ToolsListResult>(error));
    }

    public async ValueTask<Fin<ToolCallResult>> CallToolAsync(string name, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var id = NextRequestId();
        using var request = await CreatePostRequestAsync(id, McpMethods.ToolsCall, name, writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("name", name);
            if (arguments is { } supplied)
            {
                writer.WritePropertyName("arguments");
                supplied.WriteTo(writer);
            }
            writer.WriteEndObject();
        }, cancellationToken).ConfigureAwait(false);

        if (!TryApplyToolHeaders(request, name, arguments, out var headerError))
        {
            return Fin.Fail<ToolCallResult>(Error.New(headerError ?? "Failed to apply MCP tool headers."));
        }

        var response = await SendWithAuthorizationAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsFail)
        {
            return response.Match<Fin<ToolCallResult>>(Succ: _ => throw new InvalidOperationException(), Fail: error => error);
        }

        using var httpResponse = response.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException());
        CaptureSessionId(httpResponse);
        var result = await ReadHttpResponseAsync(httpResponse, id, cancellationToken).ConfigureAwait(false);
        if (result.IsFail)
        {
            return result.Match<Fin<ToolCallResult>>(Succ: _ => throw new InvalidOperationException(), Fail: error => Fin.Fail<ToolCallResult>(error));
        }

        return result.Match(
            Succ: value => Deserialize(value, McpJsonSerializerContext.Default.ToolCallResult),
            Fail: error => Fin.Fail<ToolCallResult>(error));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await TryTerminateSessionAsync().ConfigureAwait(false);
        _listenCts.Cancel();
        _inboundRouter.Dispose();
        if (_serverEventPump is not null)
        {
            try
            {
                await _serverEventPump.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _listenCts.Dispose();
        _httpClient.Dispose();
    }

    private int NextRequestId()
    {
        return Interlocked.Increment(ref _nextRequestId);
    }

    private async ValueTask<Fin<JsonElement>> SendRequestAsync(
        int id,
        string method,
        Action<Utf8JsonWriter>? writeParams,
        CancellationToken cancellationToken,
        string? mcpName = null,
        bool includeProtocolVersionHeader = true,
        bool includeSessionIdHeader = true)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var request = await CreatePostRequestAsync(
                id,
                method,
                mcpName,
                writeParams,
                cancellationToken,
                includeProtocolVersionHeader,
                includeSessionIdHeader).ConfigureAwait(false);
            var response = await SendWithAuthorizationAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsFail)
            {
                return response.Match<Fin<JsonElement>>(Succ: _ => throw new InvalidOperationException(), Fail: error => error);
            }

            using var httpResponse = response.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException());
            CaptureSessionId(httpResponse);
            return await ReadHttpResponseAsync(httpResponse, id, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async ValueTask SendNotificationAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        using var request = await CreatePostNotificationAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        var response = await SendWithAuthorizationAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSucc)
        {
            using var httpResponse = response.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException());
            CaptureSessionId(httpResponse);
        }
    }

    private async ValueTask ListenForServerMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _options.Endpoint);
                ApplyGetHeaders(request);
                if (!string.IsNullOrWhiteSpace(_lastEventId))
                {
                    request.Headers.TryAddWithoutValidation("Last-Event-ID", _lastEventId);
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.MethodNotAllowed)
                {
                    _logger.LogDebug("MCP server does not offer a standalone Streamable HTTP GET event stream.");
                    return;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    if (await TryRestartExpiredSessionAsync(cancellationToken).ConfigureAwait(false))
                    {
                        continue;
                    }

                    _logger.LogDebug("MCP server reported the active Streamable HTTP session was not found.");
                    return;
                }

                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await foreach (var sse in McpSseReader.ReadAsync(stream, cancellationToken))
                {
                    if (sse.Id is not null)
                    {
                        _lastEventId = sse.Id;
                    }

                    if (sse.RetryMilliseconds is { } retry)
                    {
                        _reconnectDelay = TimeSpan.FromMilliseconds(retry);
                    }

                    if (string.IsNullOrWhiteSpace(sse.Data))
                    {
                        continue;
                    }

                    using var document = JsonDocument.Parse(sse.Data);
                    var root = document.RootElement.Clone();
                    if (!await _inboundRouter.TryHandleAsync(root, cancellationToken).ConfigureAwait(false))
                    {
                        _logger.LogDebug("Ignoring unexpected response on standalone Streamable HTTP event stream.");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Standalone Streamable HTTP event stream closed; reconnecting.");
            }

            await Task.Delay(_reconnectDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<Fin<HttpResponseMessage>> SendWithAuthorizationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || _options.AuthorizationProvider is null)
        {
            return Fin.Succ(response);
        }

        var challenge = response.Headers.TryGetValues("WWW-Authenticate", out var values)
            ? McpWwwAuthenticateParser.TryParse(values)
            : null;
        var metadataResult = await McpProtectedResourceMetadataResolver.ResolveAsync(_httpClient, _options.Endpoint, challenge, cancellationToken).ConfigureAwait(false);
        var authorizationServers = metadataResult.IsSucc
            ? await DiscoverAuthorizationServersAsync(metadataResult.Match(Succ: static value => value, Fail: static _ => (McpProtectedResourceMetadata?)null), cancellationToken).ConfigureAwait(false)
            : Array.Empty<McpAuthorizationServerDescriptor>();
        var context = new McpAuthorizationContext
        {
            Endpoint = _options.Endpoint,
            Challenge = challenge,
            ProtectedResourceMetadata = metadataResult.IsSucc ? metadataResult.Match(Succ: static value => value, Fail: static _ => (McpProtectedResourceMetadata?)null) : null,
            AuthorizationServers = authorizationServers,
            RequiredScopes = challenge?.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>()
        };

        var token = await _options.AuthorizationProvider.GetAccessTokenAsync(context, cancellationToken).ConfigureAwait(false);
        if (token.IsFail)
        {
            response.Dispose();
            return Fin.Fail<HttpResponseMessage>(token.Match(Succ: _ => Error.New("Authorization provider failed."), Fail: error => error));
        }

        _accessToken = token.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException());
        response.Dispose();

        using var retryRequest = await CloneRequestAsync(request, cancellationToken).ConfigureAwait(false);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return Fin.Succ(await _httpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false));
    }

    private async ValueTask<IReadOnlyList<McpAuthorizationServerDescriptor>> DiscoverAuthorizationServersAsync(McpProtectedResourceMetadata? protectedResourceMetadata, CancellationToken cancellationToken)
    {
        if (protectedResourceMetadata is not { AuthorizationServers.Length: > 0 })
        {
            return Array.Empty<McpAuthorizationServerDescriptor>();
        }

        var descriptors = new List<McpAuthorizationServerDescriptor>();
        foreach (var authorizationServer in protectedResourceMetadata.AuthorizationServers)
        {
            if (!Uri.TryCreate(authorizationServer, UriKind.Absolute, out var authorizationServerUri))
            {
                _logger.LogDebug("Ignoring invalid authorization server URI '{AuthorizationServerUri}' from protected resource metadata.", authorizationServer);
                continue;
            }

            var discovered = await McpAuthorizationServerDiscovery.DiscoverAsync(_httpClient, authorizationServerUri, cancellationToken).ConfigureAwait(false);
            if (discovered.IsFail)
            {
                _logger.LogDebug("Failed to discover MCP authorization server metadata for {AuthorizationServerUri}.", authorizationServerUri);
                continue;
            }

            descriptors.Add(discovered.Match(
                Succ: static value => value,
                Fail: static _ => throw new InvalidOperationException()));
        }

        return descriptors;
    }

    private async ValueTask<Fin<JsonElement>> ReadHttpResponseAsync(HttpResponseMessage response, int expectedId, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            if (await TryRestartExpiredSessionAsync(cancellationToken).ConfigureAwait(false))
            {
                return Fin.Fail<JsonElement>(Error.New("The MCP session expired and a new session was started."));
            }

            return Fin.Fail<JsonElement>(Error.New("The MCP session was not found."));
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(payload);
            return ReadJsonRpcResponse(document.RootElement.Clone(), expectedId);
        }

        if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await foreach (var sse in McpSseReader.ReadAsync(stream, cancellationToken))
            {
                if (sse.Id is not null)
                {
                    _lastEventId = sse.Id;
                }

                if (sse.RetryMilliseconds is { } retry)
                {
                    _reconnectDelay = TimeSpan.FromMilliseconds(retry);
                }

                if (string.IsNullOrWhiteSpace(sse.Data))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(sse.Data);
                var root = document.RootElement.Clone();
                if (await _inboundRouter.TryHandleAsync(root, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var result = ReadJsonRpcResponse(root, expectedId);
                if (result.IsSucc)
                {
                    return result;
                }
            }

            return Fin.Fail<JsonElement>(Error.New($"Streamable HTTP response stream ended before JSON-RPC response {expectedId} was received."));
        }

        return Fin.Fail<JsonElement>(Error.New($"Unsupported Streamable HTTP content type '{mediaType ?? "<missing>"}'."));
    }

    private Fin<JsonElement> ReadJsonRpcResponse(JsonElement root, int expectedId)
    {
        if (!root.TryGetProperty("id"u8, out var idElement) || !idElement.TryGetInt32(out var actualId))
        {
            return Fin.Fail<JsonElement>(Error.New("MCP response is missing an integer id."));
        }

        if (actualId != expectedId)
        {
            return Fin.Fail<JsonElement>(Error.New($"Received MCP response id {actualId} while waiting for {expectedId}."));
        }

        if (root.TryGetProperty("error"u8, out var errorElement))
        {
            var message = errorElement.TryGetProperty("message"u8, out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? "Unknown MCP error."
                : "Unknown MCP error.";
            return Fin.Fail<JsonElement>(Error.New(message));
        }

        if (!root.TryGetProperty("result"u8, out var resultElement))
        {
            return Fin.Fail<JsonElement>(Error.New("MCP response is missing result or error."));
        }

        return Fin.Succ(resultElement.Clone());
    }

    private async ValueTask<Fin<InitializeResult>> SendInitializeRequestAsync(CancellationToken cancellationToken, bool acquireSendLock)
    {
        if (acquireSendLock)
        {
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var id = NextRequestId();
            using var request = await CreatePostRequestAsync(
                id,
                McpMethods.Initialize,
                null,
                WriteInitializeParams,
                cancellationToken,
                includeProtocolVersionHeader: false,
                includeSessionIdHeader: false).ConfigureAwait(false);

            var response = await SendWithAuthorizationAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsFail)
            {
                return response.Match<Fin<InitializeResult>>(Succ: _ => throw new InvalidOperationException(), Fail: error => error);
            }

            using var httpResponse = response.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException());
            CaptureSessionId(httpResponse);

            var result = await ReadHttpResponseAsync(httpResponse, id, cancellationToken).ConfigureAwait(false);
            return result.Match(
                Succ: value => Deserialize(value, McpJsonSerializerContext.Default.InitializeResult),
                Fail: error => Fin.Fail<InitializeResult>(error));
        }
        finally
        {
            if (acquireSendLock)
            {
                _sendLock.Release();
            }
        }
    }

    private async ValueTask<Fin<InitializeResult>> FinalizeInitializationAsync(Fin<InitializeResult> initializeResult, CancellationToken cancellationToken)
    {
        if (initializeResult.IsFail)
        {
            return initializeResult.Match<Fin<InitializeResult>>(Succ: _ => throw new InvalidOperationException(), Fail: error => error);
        }

        var initialized = initializeResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException());

        if (!McpProtocolVersions.IsSupported(initialized.ProtocolVersion))
        {
            return Fin.Fail<InitializeResult>(Error.New($"The MCP server negotiated unsupported protocol version '{initialized.ProtocolVersion}'."));
        }

        _negotiatedProtocolVersion = initialized.ProtocolVersion;
        await SendNotificationAsync(McpMethods.NotificationsInitialized, CreateEmptyObject(), cancellationToken).ConfigureAwait(false);
        if (_options.OpenServerEventStream && _serverEventPump is null)
        {
            _serverEventPump = Task.Run(() => ListenForServerMessagesAsync(_listenCts.Token));
        }

        return initializeResult;
    }

    private async ValueTask<bool> TryRestartExpiredSessionAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return false;
        }

        string? expiredSessionId = null;
        await _sessionRecoveryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed || _sessionId is null)
            {
                return false;
            }

            expiredSessionId = _sessionId;
            ResetSessionTrackingState_NoLock();
        }
        finally
        {
            _sessionRecoveryLock.Release();
        }

        if (_disposed)
        {
            return false;
        }

        _logger.LogInformation("MCP session {SessionId} expired; starting a new session.", expiredSessionId);
        var initializeResult = await SendInitializeRequestAsync(cancellationToken, acquireSendLock: false).ConfigureAwait(false);
        if (initializeResult.IsFail)
        {
            var error = initializeResult.Match(Succ: static _ => string.Empty, Fail: static failure => failure.Message);
            _logger.LogWarning("Failed to start a new MCP session after session expiration: {ErrorMessage}", error);
            return false;
        }

        var finalResult = await FinalizeInitializationAsync(initializeResult, cancellationToken).ConfigureAwait(false);
        return finalResult.IsSucc;
    }

    private async ValueTask TryTerminateSessionAsync()
    {
        string? sessionId;
        string? negotiatedProtocolVersion;
        await _sessionRecoveryLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            sessionId = _sessionId;
            negotiatedProtocolVersion = _negotiatedProtocolVersion;
            if (sessionId is null || string.IsNullOrWhiteSpace(negotiatedProtocolVersion))
            {
                return;
            }
        }
        finally
        {
            _sessionRecoveryLock.Release();
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, _options.Endpoint);
        ApplyCommonHeaders(request, includeSessionIdHeader: false);
        request.Headers.TryAddWithoutValidation("MCP-Session-Id", sessionId);

        using var terminationTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var response = await SendWithAuthorizationAsync(request, terminationTimeoutCts.Token).ConfigureAwait(false);
            if (response.IsSucc)
            {
                using var httpResponse = response.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException());
                _logger.LogDebug("MCP session {SessionId} termination returned HTTP {StatusCode}.", sessionId, (int)httpResponse.StatusCode);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to terminate MCP session {SessionId} cleanly.", sessionId);
        }
        finally
        {
            await _sessionRecoveryLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
                {
                    ResetSessionTrackingState_NoLock();
                }
            }
            finally
            {
                _sessionRecoveryLock.Release();
            }
        }
    }

    private void ResetSessionTrackingState_NoLock()
    {
        _sessionId = null;
        _negotiatedProtocolVersion = null;
        _lastEventId = null;
        _knownTools.Clear();
        _toolHeaderBindings.Clear();
    }

    private async ValueTask<HttpRequestMessage> CreatePostRequestAsync(int id, string method, Action<Utf8JsonWriter>? writeParams, CancellationToken cancellationToken)
    {
        return await CreatePostRequestAsync(
            id,
            method,
            null,
            writeParams,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<HttpRequestMessage> CreatePostRequestAsync(
        int id,
        string method,
        string? mcpName,
        Action<Utf8JsonWriter>? writeParams,
        CancellationToken cancellationToken,
        bool includeProtocolVersionHeader = true,
        bool includeSessionIdHeader = true)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            if (writeParams is not null)
            {
                writer.WritePropertyName("params");
                writeParams(writer);
            }

            writer.WriteEndObject();
        }

        var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        ApplyPostHeaders(
            request,
            acceptEventStream: true,
            method,
            mcpName,
            includeProtocolVersionHeader,
            includeSessionIdHeader);

        request.Content = new StringContent(Encoding.UTF8.GetString(buffer.ToArray()), Encoding.UTF8, "application/json");
        await Task.CompletedTask;
        return request;
    }

    private async ValueTask<HttpRequestMessage> CreatePostNotificationAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            parameters.WriteTo(writer);
            writer.WriteEndObject();
        }

        var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        ApplyPostHeaders(request, acceptEventStream: true, method, null);

        request.Content = new StringContent(Encoding.UTF8.GetString(buffer.ToArray()), Encoding.UTF8, "application/json");
        await Task.CompletedTask;
        return request;
    }

    private void WriteInitializeParams(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("protocolVersion", McpProtocolVersions.Current);
        writer.WritePropertyName("capabilities");
        writer.WriteStartObject();
        WriteClientCapabilities(writer);
        writer.WriteEndObject();
        writer.WritePropertyName("clientInfo");
        writer.WriteStartObject();
        writer.WriteString("name", _options.ClientName);
        writer.WriteString("title", _options.ClientTitle);
        writer.WriteString("version", _options.ClientVersion);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private void WriteClientCapabilities(Utf8JsonWriter writer)
    {
        if (_options.SupportsSampling)
        {
            writer.WritePropertyName("sampling");
            writer.WriteStartObject();
            if (_options.SupportsSamplingTools)
            {
                writer.WritePropertyName("tools");
                writer.WriteStartObject();
                writer.WriteEndObject();
            }

            if (_options.SupportsSamplingContext)
            {
                writer.WritePropertyName("context");
                writer.WriteStartObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        if (_options.SupportsElicitationForm || _options.SupportsElicitationUrl)
        {
            writer.WritePropertyName("elicitation");
            writer.WriteStartObject();
            if (_options.SupportsElicitationForm)
            {
                writer.WritePropertyName("form");
                writer.WriteStartObject();
                writer.WriteEndObject();
            }

            if (_options.SupportsElicitationUrl)
            {
                writer.WritePropertyName("url");
                writer.WriteStartObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        if (_options.SupportsTasksList || _options.SupportsTasksCancel || _options.SupportsTaskSamplingCreateMessage || _options.SupportsTaskElicitationCreate)
        {
            writer.WritePropertyName("tasks");
            writer.WriteStartObject();
            if (_options.SupportsTasksList)
            {
                writer.WritePropertyName("list");
                writer.WriteStartObject();
                writer.WriteEndObject();
            }

            if (_options.SupportsTasksCancel)
            {
                writer.WritePropertyName("cancel");
                writer.WriteStartObject();
                writer.WriteEndObject();
            }

            if (_options.SupportsTaskSamplingCreateMessage || _options.SupportsTaskElicitationCreate)
            {
                writer.WritePropertyName("requests");
                writer.WriteStartObject();
                if (_options.SupportsTaskSamplingCreateMessage)
                {
                    writer.WritePropertyName("sampling");
                    writer.WriteStartObject();
                    writer.WritePropertyName("createMessage");
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                if (_options.SupportsTaskElicitationCreate)
                {
                    writer.WritePropertyName("elicitation");
                    writer.WriteStartObject();
                    writer.WritePropertyName("create");
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }
    }

    private void ApplyPostHeaders(
        HttpRequestMessage request,
        bool acceptEventStream,
        string? method,
        string? mcpName,
        bool includeProtocolVersionHeader = true,
        bool includeSessionIdHeader = true)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (acceptEventStream)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        if (!string.IsNullOrWhiteSpace(method))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        }

        if (!string.IsNullOrWhiteSpace(mcpName))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Name", mcpName);
        }

        ApplyCommonHeaders(request, includeProtocolVersionHeader, includeSessionIdHeader);
    }

    private void ApplyGetHeaders(HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        ApplyCommonHeaders(request);
    }

    private void ApplyCommonHeaders(
        HttpRequestMessage request,
        bool includeProtocolVersionHeader = true,
        bool includeSessionIdHeader = true)
    {
        if (includeProtocolVersionHeader && !string.IsNullOrWhiteSpace(_negotiatedProtocolVersion))
        {
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _negotiatedProtocolVersion);
        }

        if (!string.IsNullOrWhiteSpace(_options.Origin))
        {
            request.Headers.TryAddWithoutValidation("Origin", _options.Origin);
        }

        if (includeSessionIdHeader && !string.IsNullOrWhiteSpace(_sessionId))
        {
            request.Headers.TryAddWithoutValidation("MCP-Session-Id", _sessionId);
        }

        foreach (var header in _options.DefaultHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (!string.IsNullOrWhiteSpace(_accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
    }

    private void CaptureSessionId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("MCP-Session-Id", out var values))
        {
            _sessionId = values.FirstOrDefault();
        }
    }

    private static JsonElement CreateEmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private async ValueTask SendResultResponseAsync(string method, JsonElement id, JsonElement result, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WritePropertyName("result");
            result.WriteTo(writer);
            writer.WriteEndObject();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        ApplyPostHeaders(request, acceptEventStream: false, method, mcpName: null);
        request.Content = new StringContent(Encoding.UTF8.GetString(buffer.ToArray()), Encoding.UTF8, "application/json");
        var response = await SendWithAuthorizationAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSucc)
        {
            using var httpResponse = response.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException());
            CaptureSessionId(httpResponse);
        }
    }

    private async ValueTask SendErrorResponseAsync(string method, JsonElement id, int code, string message, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        ApplyPostHeaders(request, acceptEventStream: false, method, mcpName: null);
        request.Content = new StringContent(Encoding.UTF8.GetString(buffer.ToArray()), Encoding.UTF8, "application/json");
        var response = await SendWithAuthorizationAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSucc)
        {
            using var httpResponse = response.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException());
            CaptureSessionId(httpResponse);
        }
    }

    private static Fin<T> Deserialize<T>(JsonElement element, JsonTypeInfo<T> jsonTypeInfo)
    {
        try
        {
            var value = element.Deserialize(jsonTypeInfo);
            return value is null
                ? Fin.Fail<T>(Error.New($"Failed to deserialize MCP result as {typeof(T).Name}."))
                : Fin.Succ(value);
        }
        catch (JsonException ex)
        {
            return Fin.Fail<T>(Error.New($"Failed to deserialize MCP result as {typeof(T).Name}: {ex.Message}"));
        }
    }

    private async ValueTask<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var payload = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            clone.Content = new StringContent(payload, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType ?? "application/json");
        }

        return clone;
    }

    private ToolsListResult FilterAndCacheTools(ToolsListResult result)
    {
        var tools = new List<McpToolDescriptor>(result.Tools.Length);
        foreach (var tool in result.Tools)
        {
            if (!TryCreateToolHeaderBindings(tool, out var bindings, out var reason))
            {
                _logger.LogWarning("Rejecting MCP tool {ToolName}: {Reason}", tool.Name, reason);
                continue;
            }

            _knownTools[tool.Name] = tool;
            _toolHeaderBindings[tool.Name] = bindings;
            tools.Add(tool);
        }

        return new ToolsListResult
        {
            Tools = tools.ToArray(),
            NextCursor = result.NextCursor
        };
    }

    private bool TryApplyToolHeaders(HttpRequestMessage request, string toolName, JsonElement? arguments, out string? error)
    {
        error = null;
        if (!_toolHeaderBindings.TryGetValue(toolName, out var bindings))
        {
            if (!_knownTools.TryGetValue(toolName, out var tool))
            {
                return true;
            }

            if (!TryCreateToolHeaderBindings(tool, out bindings, out error))
            {
                return false;
            }

            _toolHeaderBindings[toolName] = bindings;
        }

        if (bindings.Length == 0)
        {
            return true;
        }

        if (arguments is not { ValueKind: JsonValueKind.Object } suppliedArguments)
        {
            return true;
        }

        foreach (var binding in bindings)
        {
            if (!suppliedArguments.TryGetProperty(binding.ParameterName, out var valueElement) || valueElement.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (!TryFormatToolHeaderValue(valueElement, out var headerValue, out error))
            {
                return false;
            }

            request.Headers.TryAddWithoutValidation("Mcp-Param-" + binding.HeaderName, headerValue);
        }

        return true;
    }

    private static bool TryCreateToolHeaderBindings(McpToolDescriptor tool, out McpToolHeaderBinding[] bindings, out string reason)
    {
        if (tool.InputSchema.ValueKind != JsonValueKind.Object ||
            !tool.InputSchema.TryGetProperty("properties"u8, out var propertiesElement) ||
            propertiesElement.ValueKind != JsonValueKind.Object)
        {
            bindings = Array.Empty<McpToolHeaderBinding>();
            reason = string.Empty;
            return true;
        }

        var headerBindings = new List<McpToolHeaderBinding>();
        var headerNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in propertiesElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty("x-mcp-header"u8, out var headerNameElement) ||
                headerNameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var headerName = headerNameElement.GetString();
            if (string.IsNullOrWhiteSpace(headerName) || !IsValidHeaderName(headerName))
            {
                bindings = Array.Empty<McpToolHeaderBinding>();
                reason = $"Tool parameter '{property.Name}' declares an invalid x-mcp-header value.";
                return false;
            }

            if (!headerNames.Add(headerName))
            {
                bindings = Array.Empty<McpToolHeaderBinding>();
                reason = $"Tool parameter header name '{headerName}' is duplicated.";
                return false;
            }

            if (!IsPrimitiveHeaderSchema(property.Value))
            {
                bindings = Array.Empty<McpToolHeaderBinding>();
                reason = $"Tool parameter '{property.Name}' with x-mcp-header must have a primitive JSON schema type.";
                return false;
            }

            headerBindings.Add(new McpToolHeaderBinding(property.Name, headerName));
        }

        bindings = headerBindings.ToArray();
        reason = string.Empty;
        return true;
    }

    private static bool IsPrimitiveHeaderSchema(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!schema.TryGetProperty("type"u8, out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var typeName = typeElement.GetString();
        return string.Equals(typeName, "string", StringComparison.OrdinalIgnoreCase)
               || string.Equals(typeName, "number", StringComparison.OrdinalIgnoreCase)
               || string.Equals(typeName, "boolean", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidHeaderName(string value)
    {
        foreach (var character in value)
        {
            if (character is ' ' or ':' or < (char)0x21 or > (char)0x7E)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFormatToolHeaderValue(JsonElement value, out string headerValue, out string? error)
    {
        error = null;
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                headerValue = EncodeToolHeaderString(value.GetString() ?? string.Empty);
                return true;
            case JsonValueKind.Number:
                headerValue = FormatToolHeaderNumber(value);
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                headerValue = value.GetBoolean() ? "true" : "false";
                return true;
            default:
                headerValue = string.Empty;
                error = $"MCP header values must be string, number, or boolean, but '{value.ValueKind}' was supplied.";
                return false;
        }
    }

    private static string EncodeToolHeaderString(string value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (IsSafeToolHeaderValue(value))
        {
            return value;
        }

        return "=?base64?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";
    }

    private static bool IsSafeToolHeaderValue(string value)
    {
        if (value.Length != 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character == '\t')
            {
                continue;
            }

            if (character is < (char)0x20 or > (char)0x7E)
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatToolHeaderNumber(JsonElement value)
    {
        if (value.TryGetInt64(out var longValue))
        {
            return longValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetDecimal(out var decimalValue))
        {
            return decimalValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetDouble(out var doubleValue))
        {
            return doubleValue.ToString("R", CultureInfo.InvariantCulture);
        }

        return value.GetRawText();
    }

    private sealed record McpToolHeaderBinding(string ParameterName, string HeaderName);
}
