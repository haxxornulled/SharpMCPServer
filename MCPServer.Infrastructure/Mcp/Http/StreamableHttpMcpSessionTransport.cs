using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure.Mcp.JsonRpc;
using Microsoft.Extensions.Logging;

namespace MCPServer.Infrastructure.Mcp.Http;

public sealed class StreamableHttpMcpSessionTransport : IStreamableHttpMcpSessionTransport
{
    private readonly StreamableHttpMcpTransportOptions _options;
    private readonly IMcpSessionState _sessionState;
    private readonly IMcpRequestExecutionRegistry _requestExecutionRegistry;
    private readonly IJsonRpcResponseSerializer _serializer;
    private readonly ILogger<StreamableHttpMcpSessionTransport> _logger;
    private readonly object _gate = new object();
    private readonly List<OutboundMessage> _messages = new List<OutboundMessage>();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<Fin<JsonElement>>> _pendingResponses = new ConcurrentDictionary<int, TaskCompletionSource<Fin<JsonElement>>>();
    private int _oldestRetainedSequence = 1;
    private string? _sessionId;
    private int _deliveredMessageSequence;
    private int _nextSequence = 1;
    private int _nextRequestId;
    private bool _primeSent;
    private Channel<OutboundMessage>? _activeChannel;
    private CancellationTokenSource? _activeStreamAbortCts;

    public StreamableHttpMcpSessionTransport(
        StreamableHttpMcpTransportOptions options,
        IMcpSessionState sessionState,
        IMcpRequestExecutionRegistry requestExecutionRegistry,
        IJsonRpcResponseSerializer serializer,
        ILogger<StreamableHttpMcpSessionTransport> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessionState);
        ArgumentNullException.ThrowIfNull(requestExecutionRegistry);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxSessionHistoryMessages);

        _options = options;
        _sessionState = sessionState;
        _requestExecutionRegistry = requestExecutionRegistry;
        _serializer = serializer;
        _logger = logger;
    }

    public string? SessionId
    {
        get
        {
            lock (_gate)
            {
                return _sessionId;
            }
        }
    }

    public bool HasActiveSession
    {
        get
        {
            lock (_gate)
            {
                return _sessionId is not null;
            }
        }
    }

    public void StartSession()
    {
        lock (_gate)
        {
            CloseActiveStream_NoLock();
            FailPendingResponses_NoLock("The MCP session was restarted.");
            ResetSessionState_NoLock();
            _sessionId = Guid.NewGuid().ToString("N");
        }

        _sessionState.ResetSession();
        _requestExecutionRegistry.Reset();
    }

    public void TerminateSession()
    {
        lock (_gate)
        {
            CloseActiveStream_NoLock();
            FailPendingResponses_NoLock("The MCP session was terminated.");
            _sessionId = default;
            ResetSessionState_NoLock();
        }

        _sessionState.ResetSession();
        _requestExecutionRegistry.Reset();
    }

    public bool TryValidateSessionRequest(StreamableHttpMcpRequest request, bool isInitialize, out HttpStatusCode statusCode, out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionHeader = request.GetHeader(StreamableHttpMcpHeaderNames.SessionId);
        if (isInitialize)
        {
            if (!string.IsNullOrWhiteSpace(sessionHeader))
            {
                statusCode = HttpStatusCode.BadRequest;
                errorMessage = "Initialize requests must not include MCP-Session-Id.";
                return false;
            }

            statusCode = HttpStatusCode.OK;
            errorMessage = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(sessionHeader))
        {
            statusCode = HttpStatusCode.BadRequest;
            errorMessage = "Missing MCP-Session-Id header.";
            return false;
        }

        string? sessionId;
        lock (_gate)
        {
            sessionId = _sessionId;
        }

        if (sessionId is null)
        {
            statusCode = HttpStatusCode.NotFound;
            errorMessage = "Session not found.";
            return false;
        }

        if (!string.Equals(sessionHeader, sessionId, StringComparison.Ordinal))
        {
            statusCode = HttpStatusCode.NotFound;
            errorMessage = "Session not found.";
            return false;
        }

        statusCode = HttpStatusCode.OK;
        errorMessage = string.Empty;
        return true;
    }

    public bool TryHandleResponse(JsonRpcMessage message)
    {
        if (!message.IsResponse || !message.HasId)
        {
            return false;
        }

        if (!message.Id.TryGetStableKey(out var key) ||
            !int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestId))
        {
            return false;
        }

        if (!_pendingResponses.TryRemove(requestId, out var completion))
        {
            return false;
        }

        if (message.HasError)
        {
            var errorMessage = "MCP client returned a JSON-RPC error response.";
            if (message.Error is { ValueKind: JsonValueKind.Object } errorElement &&
                errorElement.TryGetProperty("message"u8, out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                errorMessage = messageElement.GetString() ?? errorMessage;
            }

            completion.TrySetResult(Fin.Fail<JsonElement>(Error.New(errorMessage)));
            return true;
        }

        if (message.Result is { } result)
        {
            completion.TrySetResult(Fin.Succ(result.Clone()));
            return true;
        }

        completion.TrySetResult(Fin.Fail<JsonElement>(Error.New("MCP client response did not include a result payload.")));
        return true;
    }

    public bool TryValidateEventStreamRequest(string? lastEventId, out HttpStatusCode statusCode, out string errorMessage)
    {
        lock (_gate)
        {
            if (_sessionId is null)
            {
                statusCode = HttpStatusCode.NotFound;
                errorMessage = "Session not found.";
                return false;
            }

            if (!TryResolveReplayStartSequence_NoLock(lastEventId, out _, out _, out var replayError))
            {
                statusCode = HttpStatusCode.Conflict;
                errorMessage = replayError;
                return false;
            }
        }

        statusCode = HttpStatusCode.OK;
        errorMessage = string.Empty;
        return true;
    }

    public async ValueTask OpenEventStreamAsync(Stream output, string? lastEventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();

        Channel<OutboundMessage> channel;
        string sessionId;
        OutboundMessage[] replayMessages;
        int replayStartSequence;
        int snapshotOldestSequence;
        int latestSequence;

        lock (_gate)
        {
            if (_sessionId is null)
            {
                throw new InvalidOperationException("The MCP session has not been initialized.");
            }

            if (_activeChannel is not null)
            {
                // A resumed connection preempts the stale stream rather than forcing the client
                // to wait for the old socket to finish timing out.
                CloseActiveStream_NoLock();
            }

            if (!TryResolveReplayStartSequence_NoLock(lastEventId, out replayStartSequence, out latestSequence, out var replayError))
            {
                throw new InvalidOperationException(replayError);
            }

            sessionId = _sessionId;
            snapshotOldestSequence = _oldestRetainedSequence;
            replayMessages = _messages.ToArray();

            channel = Channel.CreateUnbounded<OutboundMessage>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            _activeChannel = channel;
            _activeStreamAbortCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        try
        {
            var streamCancellationToken = _activeStreamAbortCts?.Token ?? cancellationToken;

            if (!string.IsNullOrWhiteSpace(lastEventId))
            {
                _primeSent = true;
            }

            if (!_primeSent)
            {
                await StreamableHttpSseWriter.WriteAsync(
                    output,
                    new StreamableHttpSseEvent
                    {
                        Id = $"{sessionId}:0",
                        Data = string.Empty,
                        RetryMilliseconds = _options.SseRetryMilliseconds
                    },
                    streamCancellationToken).ConfigureAwait(false);
                _primeSent = true;
            }

            var replayStartIndex = Math.Max(0, replayStartSequence - snapshotOldestSequence);
            for (var index = replayStartIndex; index < replayMessages.Length; index++)
            {
                var message = replayMessages[index];
                await WriteEventAsync(output, message, streamCancellationToken).ConfigureAwait(false);
            }

            lock (_gate)
            {
                _deliveredMessageSequence = Math.Max(_deliveredMessageSequence, latestSequence);
            }

            await PumpLiveMessagesAsync(output, channel.Reader, streamCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeChannel, channel))
                {
                    CloseActiveStream_NoLock();
                }
            }
        }
    }

    public async ValueTask<Fin<JsonElement>> CreateMessageAsync(CreateMessageRequestParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!IsClientReadyForRequests())
        {
            return Fin.Fail<JsonElement>(Error.New("The connected client has not completed MCP initialization yet."));
        }

        if (!_sessionState.ClientCapabilities.SupportsSampling)
        {
            return Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate sampling/createMessage support during initialize."));
        }

        if (parameters.Task is not null && !_sessionState.ClientCapabilities.TasksSupportsSamplingCreateMessage)
        {
            return Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate task-augmented sampling/createMessage support."));
        }

        var payload = JsonSerializer.SerializeToElement(parameters, McpJsonSerializerContext.Default.CreateMessageRequestParams);
        return await SendRequestAsync(McpMethods.SamplingCreateMessage, payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Fin<JsonElement>> ElicitFormAsync(ElicitRequestFormParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!IsClientReadyForRequests())
        {
            return Fin.Fail<JsonElement>(Error.New("The connected client has not completed MCP initialization yet."));
        }

        if (!_sessionState.ClientCapabilities.ElicitationSupportsForm)
        {
            return Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate elicitation/create form support during initialize."));
        }

        if (parameters.Task is not null && !_sessionState.ClientCapabilities.TasksSupportsElicitationCreate)
        {
            return Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate task-augmented elicitation/create support."));
        }

        var payload = JsonSerializer.SerializeToElement(parameters, McpJsonSerializerContext.Default.ElicitRequestFormParams);
        return await SendRequestAsync(McpMethods.ElicitationCreate, payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Fin<JsonElement>> ElicitUrlAsync(ElicitRequestUrlParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!IsClientReadyForRequests())
        {
            return Fin.Fail<JsonElement>(Error.New("The connected client has not completed MCP initialization yet."));
        }

        if (!_sessionState.ClientCapabilities.ElicitationSupportsUrl)
        {
            return Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate elicitation/create URL support during initialize."));
        }

        if (parameters.Task is not null && !_sessionState.ClientCapabilities.TasksSupportsElicitationCreate)
        {
            return Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate task-augmented elicitation/create support."));
        }

        var payload = JsonSerializer.SerializeToElement(parameters, McpJsonSerializerContext.Default.ElicitRequestUrlParams);
        return await SendRequestAsync(McpMethods.ElicitationCreate, payload, cancellationToken).ConfigureAwait(false);
    }

    public void Publish(TaskStatusNotificationParams taskStatus)
    {
        ArgumentNullException.ThrowIfNull(taskStatus);

        if (!_sessionState.ClientCapabilities.SupportsTasks)
        {
            return;
        }

        _ = QueueNotificationAsync(McpMethods.NotificationsTasksStatus, JsonSerializer.SerializeToElement(taskStatus, McpJsonSerializerContext.Default.TaskStatusNotificationParams));
    }

    private async ValueTask<Fin<JsonElement>> SendRequestAsync(string method, JsonElement payload, CancellationToken cancellationToken)
    {
        var channel = ActiveChannel;
        if (channel is null)
        {
            return Fin.Fail<JsonElement>(Error.New("No active Streamable HTTP SSE stream is available for outbound client feature requests."));
        }

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<Fin<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResponses[requestId] = completion;

        try
        {
            var payloadJson = await SerializeRequestAsync(method, payload, requestId, cancellationToken).ConfigureAwait(false);
            var message = AppendMessage(payloadJson, OutboundMessageKind.Request);
            if (!channel.Writer.TryWrite(message))
            {
                _pendingResponses.TryRemove(requestId, out _);
                return Fin.Fail<JsonElement>(Error.New($"Failed to send outbound MCP request '{method}'."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _pendingResponses.TryRemove(requestId, out _);
            throw;
        }
        catch (Exception ex)
        {
            _pendingResponses.TryRemove(requestId, out _);
            return Fin.Fail<JsonElement>(Error.New($"Failed to send outbound MCP request '{method}': {ex.Message}"));
        }

        using var registration = cancellationToken.Register(static state =>
        {
            if (state is TaskCompletionSource<Fin<JsonElement>> completionSource)
            {
                completionSource.TrySetCanceled();
            }
        }, completion);

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _pendingResponses.TryRemove(requestId, out _);
            return Fin.Fail<JsonElement>(Error.New($"Outbound MCP request '{method}' was cancelled."));
        }
    }

    private async ValueTask QueueNotificationAsync(string method, JsonElement payload)
    {
        var channel = ActiveChannel;
        var payloadJson = await SerializeNotificationAsync(method, payload, CancellationToken.None).ConfigureAwait(false);
        var message = AppendMessage(payloadJson, OutboundMessageKind.Notification);

        if (channel is not null)
        {
            channel.Writer.TryWrite(message);
        }
        else
        {
            _logger.LogDebug("Buffered MCP notification {Method} until a Streamable HTTP SSE stream is opened.", method);
        }
    }

    private async ValueTask PumpLiveMessagesAsync(Stream output, ChannelReader<OutboundMessage> reader, CancellationToken cancellationToken)
    {
        try
        {
            var heartbeatInterval = _options.SseHeartbeatMilliseconds;
            if (heartbeatInterval <= 0)
            {
                await foreach (var message in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    await WriteEventAsync(output, message, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            var heartbeatDelay = TimeSpan.FromMilliseconds(heartbeatInterval);
            var readTask = reader.WaitToReadAsync(cancellationToken).AsTask();

            while (true)
            {
                var heartbeatTask = Task.Delay(heartbeatDelay, cancellationToken);
                var completedTask = await Task.WhenAny(readTask, heartbeatTask).ConfigureAwait(false);

                if (completedTask == heartbeatTask)
                {
                    await StreamableHttpSseWriter.WriteCommentAsync(output, "keep-alive", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!await readTask.ConfigureAwait(false))
                {
                    break;
                }

                while (reader.TryRead(out var message))
                {
                    await WriteEventAsync(output, message, cancellationToken).ConfigureAwait(false);
                }

                readTask = reader.WaitToReadAsync(cancellationToken).AsTask();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask WriteEventAsync(Stream output, OutboundMessage message, CancellationToken cancellationToken)
    {
        var sessionId = SessionId;
        if (sessionId is null)
        {
            return;
        }

        await StreamableHttpSseWriter.WriteAsync(
            output,
            new StreamableHttpSseEvent
            {
                Id = $"{sessionId}:{message.Sequence.ToString(CultureInfo.InvariantCulture)}",
                Data = message.Payload
            },
            cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _deliveredMessageSequence = Math.Max(_deliveredMessageSequence, message.Sequence);
        }
    }

    private OutboundMessage AppendMessage(string payload, OutboundMessageKind kind)
    {
        lock (_gate)
        {
            var message = new OutboundMessage(_nextSequence++, payload, kind);
            _messages.Add(message);
            TrimMessageHistory_NoLock();
            return message;
        }
    }

    private async ValueTask<string> SerializeRequestAsync(string method, JsonElement payload, int requestId, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await _serializer.WriteRequestAsync(buffer, requestId, method, payload, cancellationToken).ConfigureAwait(false);
        return TrimSerializedFrame(buffer);
    }

    private async ValueTask<string> SerializeNotificationAsync(string method, JsonElement payload, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await _serializer.WriteNotificationAsync(buffer, method, payload, cancellationToken).ConfigureAwait(false);
        return TrimSerializedFrame(buffer);
    }

    private static string TrimSerializedFrame(MemoryStream buffer)
    {
        var text = Encoding.UTF8.GetString(buffer.ToArray());
        return text.TrimEnd('\r', '\n');
    }

    private bool IsClientReadyForRequests()
    {
        return _sessionState.InitializeResponseSent && _sessionState.InitializedNotificationReceived;
    }

    private bool TryResolveReplayStartSequence_NoLock(string? lastEventId, out int startSequence, out int latestSequence, out string errorMessage)
    {
        startSequence = 0;
        latestSequence = _nextSequence - 1;
        errorMessage = string.Empty;
        var oldestRetainedSequence = _messages.Count > 0 ? _messages[0].Sequence : _nextSequence;

        if (string.IsNullOrWhiteSpace(lastEventId))
        {
            startSequence = Math.Max(_deliveredMessageSequence + 1, oldestRetainedSequence);
            return true;
        }

        if (!TryParseEventId(lastEventId, out var prefix, out var sequence))
        {
            errorMessage = "Last-Event-ID is malformed.";
            return false;
        }

        var sessionId = _sessionId;
        if (sessionId is null || !string.Equals(prefix, sessionId, StringComparison.Ordinal))
        {
            errorMessage = "Last-Event-ID does not match the active MCP session.";
            return false;
        }

        if (sequence < 0 || sequence == int.MaxValue)
        {
            errorMessage = "Last-Event-ID is malformed.";
            return false;
        }

        if (sequence > latestSequence)
        {
            errorMessage = "Requested SSE replay cursor is beyond the available session history.";
            return false;
        }

        startSequence = sequence + 1;
        if (startSequence < oldestRetainedSequence)
        {
            errorMessage = "Requested SSE replay cursor is beyond the available session history.";
            return false;
        }

        return true;
    }

    private static bool TryParseEventId(string value, out string prefix, out int sequence)
    {
        prefix = string.Empty;
        sequence = 0;

        var separatorIndex = value.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        prefix = value[..separatorIndex];
        var sequenceText = value[(separatorIndex + 1)..];
        return int.TryParse(sequenceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);
    }

    private Channel<OutboundMessage>? ActiveChannel
    {
        get
        {
            lock (_gate)
            {
                return _activeChannel;
            }
        }
    }

    private void CloseActiveStream_NoLock()
    {
        try
        {
            _activeStreamAbortCts?.Cancel();
        }
        catch
        {
        }

        _activeStreamAbortCts?.Dispose();
        _activeStreamAbortCts = null;
        _activeChannel = null;
    }

    private void FailPendingResponses_NoLock(string message)
    {
        foreach (var pair in _pendingResponses)
        {
            if (_pendingResponses.TryRemove(pair.Key, out var completion))
            {
                completion.TrySetResult(Fin.Fail<JsonElement>(Error.New(message)));
            }
        }
    }

    private void ResetSessionState_NoLock()
    {
        _messages.Clear();
        _oldestRetainedSequence = 1;
        _deliveredMessageSequence = 0;
        _nextSequence = 1;
        _primeSent = false;
    }

    private void TrimMessageHistory_NoLock()
    {
        var excessMessageCount = _messages.Count - _options.MaxSessionHistoryMessages;
        if (excessMessageCount <= 0)
        {
            return;
        }

        // Keep the retained SSE history bounded so reconnect replay cannot grow memory without limit.
        _messages.RemoveRange(0, excessMessageCount);
        _oldestRetainedSequence = _messages.Count > 0 ? _messages[0].Sequence : _nextSequence;
    }

    private readonly record struct OutboundMessage(int Sequence, string Payload, OutboundMessageKind Kind);

    private enum OutboundMessageKind
    {
        Notification,
        Request
    }
}
