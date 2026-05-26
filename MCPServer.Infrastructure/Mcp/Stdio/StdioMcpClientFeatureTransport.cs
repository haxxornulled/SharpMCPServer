using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Infrastructure.Mcp.Stdio;

public sealed class StdioMcpClientFeatureTransport : IMcpClientFeatureInvoker, IMcpTaskStatusNotifier, IStdioMcpClientFeatureTransport
{
    private readonly IMcpSessionState _sessionState;
    private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<Fin<JsonElement>>> _pendingResponses = new();
    private Stream? _output;
    private IJsonRpcResponseSerializer? _serializer;
    private int _nextRequestId;

    public StdioMcpClientFeatureTransport(IMcpSessionState sessionState)
    {
        _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
    }

    public void Attach(Stream output, IJsonRpcResponseSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(serializer);
        _output = output;
        _serializer = serializer;
    }

    public bool TryHandleResponse(JsonRpcMessage message)
    {
        if (!message.IsResponse || !message.HasId)
        {
            return false;
        }

        if (!message.Id.TryGetStableKey(out var key) || !int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        if (!_pendingResponses.TryRemove(id, out var completion))
        {
            return false;
        }

        if (message.HasError)
        {
            var messageText = "MCP client returned a JSON-RPC error response.";
            if (message.Error is { ValueKind: JsonValueKind.Object } errorElement &&
                errorElement.TryGetProperty("message"u8, out var errorMessageElement) &&
                errorMessageElement.ValueKind == JsonValueKind.String)
            {
                messageText = errorMessageElement.GetString() ?? messageText;
            }

            completion.TrySetResult(Fin.Fail<JsonElement>(Error.New(messageText)));
        }
        else if (message.Result is { } result)
        {
            completion.TrySetResult(Fin.Succ(result.Clone()));
        }
        else
        {
            completion.TrySetResult(Fin.Fail<JsonElement>(Error.New("MCP client response did not include a result payload.")));
        }

        return true;
    }

    public ValueTask<Fin<JsonElement>> CreateMessageAsync(CreateMessageRequestParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!_sessionState.ClientCapabilities.SupportsSampling)
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate sampling support during initialize.")));
        }

        if (parameters.Task is not null && !_sessionState.ClientCapabilities.TasksSupportsSamplingCreateMessage)
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate task-augmented sampling/createMessage support.")));
        }

        var payload = JsonSerializer.SerializeToElement(parameters, McpJsonSerializerContext.Default.CreateMessageRequestParams);
        return SendRequestAsync(McpMethods.SamplingCreateMessage, payload, cancellationToken);
    }

    public ValueTask<Fin<JsonElement>> ElicitFormAsync(ElicitRequestFormParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!_sessionState.ClientCapabilities.ElicitationSupportsForm)
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate elicitation form support during initialize.")));
        }

        if (parameters.Task is not null && !_sessionState.ClientCapabilities.TasksSupportsElicitationCreate)
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate task-augmented elicitation/create support.")));
        }

        var payload = JsonSerializer.SerializeToElement(parameters, McpJsonSerializerContext.Default.ElicitRequestFormParams);
        return SendRequestAsync(McpMethods.ElicitationCreate, payload, cancellationToken);
    }

    public ValueTask<Fin<JsonElement>> ElicitUrlAsync(ElicitRequestUrlParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!_sessionState.ClientCapabilities.ElicitationSupportsUrl)
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate elicitation URL support during initialize.")));
        }

        if (parameters.Task is not null && !_sessionState.ClientCapabilities.TasksSupportsElicitationCreate)
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New("The connected client did not negotiate task-augmented elicitation/create support.")));
        }

        var payload = JsonSerializer.SerializeToElement(parameters, McpJsonSerializerContext.Default.ElicitRequestUrlParams);
        return SendRequestAsync(McpMethods.ElicitationCreate, payload, cancellationToken);
    }

    public void Publish(TaskStatusNotificationParams taskStatus)
    {
        ArgumentNullException.ThrowIfNull(taskStatus);

        if (!_sessionState.ClientCapabilities.SupportsTasks || _output is null || _serializer is null)
        {
            return;
        }

        _ = PublishCoreAsync(taskStatus);
    }

    private async Task PublishCoreAsync(TaskStatusNotificationParams taskStatus)
    {
        try
        {
            var output = _output;
            var serializer = _serializer;
            if (output is null || serializer is null)
            {
                return;
            }

            var payload = JsonSerializer.SerializeToElement(taskStatus, McpJsonSerializerContext.Default.TaskStatusNotificationParams);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await serializer.WriteNotificationAsync(output, McpMethods.NotificationsTasksStatus, payload, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch
        {
        }
    }

    private async ValueTask<Fin<JsonElement>> SendRequestAsync(string method, JsonElement payload, CancellationToken cancellationToken)
    {
        var output = _output;
        var serializer = _serializer;
        if (output is null || serializer is null)
        {
            return Fin.Fail<JsonElement>(Error.New("No active stdio MCP client connection is available for outbound client feature requests."));
        }

        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<Fin<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResponses[id] = completion;

        try
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await serializer.WriteRequestAsync(output, id, method, payload, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _pendingResponses.TryRemove(id, out _);
                throw;
            }
            catch (Exception ex)
            {
                _pendingResponses.TryRemove(id, out _);
                return Fin.Fail<JsonElement>(Error.New($"Failed to send outbound MCP request '{method}': {ex.Message}"));
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _pendingResponses.TryRemove(id, out _);
            throw;
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
            _pendingResponses.TryRemove(id, out _);
            return Fin.Fail<JsonElement>(Error.New($"Outbound MCP request '{method}' was cancelled."));
        }
    }
}
