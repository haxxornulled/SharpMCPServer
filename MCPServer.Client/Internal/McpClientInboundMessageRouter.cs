using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LanguageExt;
using MCPServer.Client.Interfaces;
using MCPServer.Domain.Mcp;
using Microsoft.Extensions.Logging;

namespace MCPServer.Client.Internal;

internal sealed class McpClientInboundMessageRouter : IDisposable
{
    private readonly IMcpClientSamplingHandler? _samplingHandler;
    private readonly IMcpClientElicitationHandler? _elicitationHandler;
    private readonly IMcpClientTaskRegistry _taskRegistry;
    private readonly IMcpTaskStatusObserver? _taskStatusObserver;
    private readonly Func<string, JsonElement, JsonElement, CancellationToken, ValueTask> _writeResultAsync;
    private readonly Func<string, JsonElement, int, string, CancellationToken, ValueTask> _writeErrorAsync;
    private readonly Func<string, JsonElement, CancellationToken, ValueTask> _writeNotificationAsync;
    private readonly ILogger _logger;

    public McpClientInboundMessageRouter(
        IMcpClientSamplingHandler? samplingHandler,
        IMcpClientElicitationHandler? elicitationHandler,
        IMcpClientTaskRegistry taskRegistry,
        IMcpTaskStatusObserver? taskStatusObserver,
        Func<string, JsonElement, JsonElement, CancellationToken, ValueTask> writeResultAsync,
        Func<string, JsonElement, int, string, CancellationToken, ValueTask> writeErrorAsync,
        Func<string, JsonElement, CancellationToken, ValueTask> writeNotificationAsync,
        ILogger logger)
    {
        _samplingHandler = samplingHandler;
        _elicitationHandler = elicitationHandler;
        _taskRegistry = taskRegistry ?? throw new ArgumentNullException(nameof(taskRegistry));
        _taskStatusObserver = taskStatusObserver;
        _writeResultAsync = writeResultAsync ?? throw new ArgumentNullException(nameof(writeResultAsync));
        _writeErrorAsync = writeErrorAsync ?? throw new ArgumentNullException(nameof(writeErrorAsync));
        _writeNotificationAsync = writeNotificationAsync ?? throw new ArgumentNullException(nameof(writeNotificationAsync));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _taskRegistry.TaskStatusChanged += OnTaskStatusChanged;
    }

    public async ValueTask<bool> TryHandleAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("method"u8, out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var method = methodElement.GetString();
        if (string.IsNullOrWhiteSpace(method))
        {
            return false;
        }

        var hasId = message.TryGetProperty("id"u8, out var idElement);
        var parameters = message.TryGetProperty("params"u8, out var paramsElement)
            ? paramsElement.Clone()
            : CreateEmptyObject();

        if (!hasId)
        {
            await HandleNotificationAsync(method, parameters, cancellationToken).ConfigureAwait(false);
            return true;
        }

        var responseId = idElement.Clone();
        await HandleRequestAsync(method, responseId, parameters, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public void Dispose()
    {
        _taskRegistry.TaskStatusChanged -= OnTaskStatusChanged;
    }

    private async ValueTask HandleRequestAsync(string method, JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case McpMethods.Ping:
                await _writeResultAsync(McpMethods.Ping, id, CreateEmptyObject(), cancellationToken).ConfigureAwait(false);
                return;
            case McpMethods.SamplingCreateMessage:
                await HandleSamplingAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                return;
            case McpMethods.ElicitationCreate:
                await HandleElicitationAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                return;
            case McpMethods.TasksList:
                await HandleTasksListAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                return;
            case McpMethods.TasksGet:
                await HandleTaskGetAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                return;
            case McpMethods.TasksResult:
                await HandleTaskResultAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                return;
            case McpMethods.TasksCancel:
                await HandleTaskCancelAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                return;
            default:
                await _writeErrorAsync(method, id, JsonRpcErrorCodes.MethodNotFound, $"Method '{method}' is not supported by this MCP client.", cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async ValueTask HandleSamplingAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (_samplingHandler is null)
        {
            await _writeErrorAsync(McpMethods.SamplingCreateMessage, id, JsonRpcErrorCodes.MethodNotFound, "No sampling handler is configured for this MCP client.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var parsed = Deserialize(parameters, McpJsonSerializerContext.Default.CreateMessageRequestParams);
        if (parsed.IsFail)
        {
            await _writeErrorAsync(McpMethods.SamplingCreateMessage, id, JsonRpcErrorCodes.InvalidParams, parsed.Match(Succ: _ => string.Empty, Fail: error => error.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        var response = await _samplingHandler.HandleAsync(parsed.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException()), _taskRegistry, cancellationToken).ConfigureAwait(false);
        if (response.IsFail)
        {
            await _writeErrorAsync(McpMethods.SamplingCreateMessage, id, JsonRpcErrorCodes.InternalError, response.Match(Succ: _ => string.Empty, Fail: error => error.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        await _writeResultAsync(McpMethods.SamplingCreateMessage, id, response.Match(Succ: value => value.ToJsonElement(), Fail: _ => throw new InvalidOperationException()), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleElicitationAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (_elicitationHandler is null)
        {
            await _writeErrorAsync(McpMethods.ElicitationCreate, id, JsonRpcErrorCodes.MethodNotFound, "No elicitation handler is configured for this MCP client.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var isUrlMode = parameters.TryGetProperty("mode"u8, out var modeElement) &&
                        modeElement is { ValueKind: JsonValueKind.String } &&
                        string.Equals(modeElement.GetString(), "url", StringComparison.OrdinalIgnoreCase);
        if (!isUrlMode && parameters.TryGetProperty("url"u8, out _))
        {
            isUrlMode = true;
        }

        if (isUrlMode)
        {
            var parsed = Deserialize(parameters, McpJsonSerializerContext.Default.ElicitRequestUrlParams);
            if (parsed.IsFail)
            {
                await _writeErrorAsync(McpMethods.ElicitationCreate, id, JsonRpcErrorCodes.InvalidParams, parsed.Match(Succ: _ => string.Empty, Fail: error => error.Message), cancellationToken).ConfigureAwait(false);
                return;
            }

            var response = await _elicitationHandler.HandleUrlAsync(parsed.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException()), _taskRegistry, cancellationToken).ConfigureAwait(false);
            if (response.IsFail)
            {
                await _writeErrorAsync(McpMethods.ElicitationCreate, id, JsonRpcErrorCodes.InternalError, response.Match(Succ: _ => string.Empty, Fail: error => error.Message), cancellationToken).ConfigureAwait(false);
                return;
            }

            await _writeResultAsync(McpMethods.ElicitationCreate, id, response.Match(Succ: value => value.ToJsonElement(), Fail: _ => throw new InvalidOperationException()), cancellationToken).ConfigureAwait(false);
            return;
        }

        var formParsed = Deserialize(parameters, McpJsonSerializerContext.Default.ElicitRequestFormParams);
        if (formParsed.IsFail)
        {
            await _writeErrorAsync(McpMethods.ElicitationCreate, id, JsonRpcErrorCodes.InvalidParams, formParsed.Match(Succ: _ => string.Empty, Fail: error => error.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        var formResponse = await _elicitationHandler.HandleFormAsync(formParsed.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException()), _taskRegistry, cancellationToken).ConfigureAwait(false);
        if (formResponse.IsFail)
        {
            await _writeErrorAsync(McpMethods.ElicitationCreate, id, JsonRpcErrorCodes.InternalError, formResponse.Match(Succ: _ => string.Empty, Fail: error => error.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        await _writeResultAsync(McpMethods.ElicitationCreate, id, formResponse.Match(Succ: value => value.ToJsonElement(), Fail: _ => throw new InvalidOperationException()), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleTasksListAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var cursor = parameters.TryGetProperty("cursor"u8, out var cursorElement) && cursorElement is { ValueKind: JsonValueKind.String }
            ? cursorElement.GetString()
            : null;
        var result = _taskRegistry.ListTasks(cursor);
        if (result.IsFail)
        {
            await _writeErrorAsync(McpMethods.TasksList, id, JsonRpcErrorCodes.InvalidParams, result.Match(Succ: _ => string.Empty, Fail: error => error.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.SerializeToElement(result.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException()), McpJsonSerializerContext.Default.ListTasksResult);
        await _writeResultAsync(McpMethods.TasksList, id, payload, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleTaskGetAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var taskId = ReadTaskId(parameters);
        if (taskId is null)
        {
            await _writeErrorAsync(McpMethods.TasksGet, id, JsonRpcErrorCodes.InvalidParams, "tasks/get requires a string taskId parameter.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = _taskRegistry.GetTask(taskId);
        if (result.IsFail)
        {
            await _writeErrorAsync(McpMethods.TasksGet, id, JsonRpcErrorCodes.InvalidParams, result.Match(Succ: _ => string.Empty, Fail: error => error.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.SerializeToElement(result.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException()), McpJsonSerializerContext.Default.GetTaskResult);
        await _writeResultAsync(McpMethods.TasksGet, id, payload, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleTaskResultAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var taskId = ReadTaskId(parameters);
        if (taskId is null)
        {
            await _writeErrorAsync(McpMethods.TasksResult, id, JsonRpcErrorCodes.InvalidParams, "tasks/result requires a string taskId parameter.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = _taskRegistry.GetTaskResultPayload(taskId);
        if (result.IsFail)
        {
            await _writeErrorAsync(McpMethods.TasksResult, id, JsonRpcErrorCodes.InvalidParams, result.Match(Succ: _ => string.Empty, Fail: error => error.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        await _writeResultAsync(McpMethods.TasksResult, id, result.Match(Succ: value => value.Clone(), Fail: _ => throw new InvalidOperationException()), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleTaskCancelAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var taskId = ReadTaskId(parameters);
        if (taskId is null)
        {
            await _writeErrorAsync(McpMethods.TasksCancel, id, JsonRpcErrorCodes.InvalidParams, "tasks/cancel requires a string taskId parameter.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = _taskRegistry.CancelTask(taskId);
        if (result.IsFail)
        {
            await _writeErrorAsync(McpMethods.TasksCancel, id, JsonRpcErrorCodes.InvalidParams, result.Match(Succ: _ => string.Empty, Fail: error => error.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.SerializeToElement(result.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException()), McpJsonSerializerContext.Default.CancelTaskResult);
        await _writeResultAsync(McpMethods.TasksCancel, id, payload, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleNotificationAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case McpMethods.NotificationsTasksStatus:
                if (_taskStatusObserver is null)
                {
                    return;
                }

                var parsed = Deserialize(parameters, McpJsonSerializerContext.Default.TaskStatusNotificationParams);
                if (parsed.IsFail)
                {
                    return;
                }

                await _taskStatusObserver.OnTaskStatusAsync(parsed.Match(Succ: value => value, Fail: _ => throw new InvalidOperationException()), cancellationToken).ConfigureAwait(false);

                return;
            default:
                _logger.LogDebug("Ignoring inbound MCP client notification {Method}.", method);
                return;
        }
    }

    private void OnTaskStatusChanged(object? sender, TaskStatusNotificationParams taskStatus)
    {
        _ = PublishTaskStatusAsync(taskStatus);
    }

    private async Task PublishTaskStatusAsync(TaskStatusNotificationParams taskStatus)
    {
        try
        {
            var payload = JsonSerializer.SerializeToElement(taskStatus, McpJsonSerializerContext.Default.TaskStatusNotificationParams);
            await _writeNotificationAsync(McpMethods.NotificationsTasksStatus, payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to publish client-owned MCP task status notification.");
        }
    }

    private static string? ReadTaskId(JsonElement parameters)
    {
        return parameters.TryGetProperty("taskId"u8, out var taskIdElement) && taskIdElement is { ValueKind: JsonValueKind.String }
            ? taskIdElement.GetString()
            : null;
    }

    private static JsonElement CreateEmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static Fin<T> Deserialize<T>(JsonElement element, JsonTypeInfo<T> jsonTypeInfo)
    {
        try
        {
            return element.Deserialize(jsonTypeInfo) is { } value
                ? Fin.Succ(value)
                : Fin.Fail<T>(LanguageExt.Common.Error.New($"Failed to deserialize MCP payload as {typeof(T).Name}."));
        }
        catch (JsonException ex)
        {
            return Fin.Fail<T>(LanguageExt.Common.Error.New($"Failed to deserialize MCP payload as {typeof(T).Name}: {ex.Message}"));
        }
    }
}
