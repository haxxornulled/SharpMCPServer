using System.Text.Json;
using Autofac.Features.Indexed;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using Microsoft.Extensions.Logging;

namespace MCPServer.Application.Mcp;

public sealed class McpRequestDispatcher : IMcpRequestDispatcher
{
    private const string JsonRpcVersion = "2.0";

    private readonly IIndex<string, IMcpMethodHandler> _handlers;
    private readonly IMcpSessionState _sessionState;
    private readonly IMcpRequestExecutionRegistry _requestExecutionRegistry;
    private readonly ILogger<McpRequestDispatcher> _logger;

    public McpRequestDispatcher(
        IIndex<string, IMcpMethodHandler> handlers,
        IMcpSessionState sessionState,
        IMcpRequestExecutionRegistry requestExecutionRegistry,
        ILogger<McpRequestDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(sessionState);
        ArgumentNullException.ThrowIfNull(requestExecutionRegistry);
        ArgumentNullException.ThrowIfNull(logger);

        _handlers = handlers;
        _sessionState = sessionState;
        _requestExecutionRegistry = requestExecutionRegistry;
        _logger = logger;
    }

    public async ValueTask<Fin<JsonRpcDispatchResult>> DispatchAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        if (!string.Equals(message.JsonRpc, JsonRpcVersion, StringComparison.Ordinal))
        {
            return Respond(JsonRpcResponse.Failure(GetResponseId(message), JsonRpcErrorCodes.InvalidRequest, "Invalid JSON-RPC version."));
        }

        if (message.IsResponse)
        {
            // This server does not issue client-bound requests yet, so there is nothing to correlate.
            // JSON-RPC responses are terminal messages and MUST NOT be answered with another response.
            return NoResponse();
        }

        if (message.Method is not { } method || string.IsNullOrWhiteSpace(method))
        {
            return message.HasId
                ? Respond(JsonRpcResponse.Failure(GetResponseId(message), JsonRpcErrorCodes.InvalidRequest, "Missing JSON-RPC method."))
                : NoResponse();
        }

        if (IsReservedJsonRpcMethod(method))
        {
            return message.HasId
                ? Respond(JsonRpcResponse.Failure(GetResponseId(message), JsonRpcErrorCodes.InvalidRequest, "JSON-RPC method names starting with 'rpc.' are reserved."))
                : NoResponse();
        }

        if (message.Params is { } parameters && parameters is not { ValueKind: JsonValueKind.Object })
        {
            return message.HasId
                ? Respond(JsonRpcResponse.Failure(GetResponseId(message), JsonRpcErrorCodes.InvalidRequest, "MCP params must be a JSON object when present."))
                : NoResponse();
        }

        if (message.Params is { ValueKind: JsonValueKind.Object } parameterObject && !TryValidateMeta(parameterObject, out var metaError))
        {
            return message.HasId
                ? Respond(JsonRpcResponse.Failure(GetResponseId(message), JsonRpcErrorCodes.InvalidParams, metaError))
                : NoResponse();
        }

        if (IsMcpNotificationMethod(method) && message.HasId)
        {
            return Respond(JsonRpcResponse.Failure(GetResponseId(message), JsonRpcErrorCodes.InvalidRequest, "MCP notifications MUST NOT include an id."));
        }

        if (message.HasId && !_sessionState.TryRegisterClientRequestId(message.Id))
        {
            return Respond(JsonRpcResponse.Failure(GetResponseId(message), JsonRpcErrorCodes.InvalidRequest, "JSON-RPC request id has already been used in this session."));
        }

        if (ValidateLifecycle(method) is { } lifecycleFailure)
        {
            return message.HasId
                ? Respond(JsonRpcResponse.Failure(GetResponseId(message), JsonRpcErrorCodes.InvalidRequest, lifecycleFailure))
                : NoResponse();
        }

        if (!_handlers.TryGetValue(method, out var handler))
        {
            return message.HasId
                ? Respond(JsonRpcResponse.Failure(GetResponseId(message), JsonRpcErrorCodes.MethodNotFound, $"Method '{method}' was not found."))
                : NoResponse();
        }

        var requestScopeResult = _requestExecutionRegistry.Register(message, cancellationToken).Match(
            Succ: static scope => RequestScopeRegistration.Success(scope),
            Fail: static error => RequestScopeRegistration.Fail(error));

        if (requestScopeResult is not { IsSuccess: true })
        {
            var errorMessage = requestScopeResult.Error is { } requestScopeError
                ? requestScopeError.Message
                : "MCP request registration failed.";

            return message.HasId
                ? Respond(JsonRpcResponse.Failure(GetResponseId(message), JsonRpcErrorCodes.InvalidParams, errorMessage))
                : NoResponse();
        }

        using var requestScope = requestScopeResult.Scope;
        var effectiveCancellationToken = requestScope.CancellationToken;

        try
        {
            var outcome = (await handler.HandleAsync(message.Params, effectiveCancellationToken).ConfigureAwait(false)).Match(
                Succ: static payload => HandlerOutcome.Success(payload),
                Fail: static error => HandlerOutcome.Fail(error));

            if (!message.HasId)
            {
                return NoResponse();
            }

            return outcome is { IsSuccess: true }
                ? Respond(JsonRpcResponse.Success(GetResponseId(message), outcome.Payload))
                : Respond(JsonRpcResponse.Failure(
                    GetResponseId(message),
                    GetHandlerFailureCode(method, outcome.Error),
                    outcome.Error is { } handlerError ? handlerError.Message : "MCP handler failed."));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON parameters for MCP method {Method}", method);

            return message.HasId
                ? Respond(JsonRpcResponse.Failure(GetResponseId(message), JsonRpcErrorCodes.InvalidParams, ex.Message))
                : NoResponse();
        }
        catch (OperationCanceledException) when (effectiveCancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("MCP method {Method} was cancelled", method);
            return NoResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while dispatching MCP method {Method}", method);
            return Fin.Fail<JsonRpcDispatchResult>(Error.New($"Unhandled MCP dispatch failure for method '{method}'."));
        }
    }

    private static int GetHandlerFailureCode(string method, Error? error)
    {
        if (method is McpMethods.ResourcesRead or McpMethods.ResourcesSubscribe or McpMethods.ResourcesUnsubscribe &&
            error is { Message: { } message } &&
            message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return JsonRpcErrorCodes.ResourceNotFound;
        }

        return JsonRpcErrorCodes.InvalidParams;
    }

    private string? ValidateLifecycle(string method)
    {
        return (_sessionState.InitializeResponseSent, _sessionState.InitializedNotificationReceived, method) switch
        {
            (false, _, var requestedMethod) when !IsAllowedBeforeInitialize(requestedMethod) => McpErrorMessages.SessionNotInitialized,
            (true, _, McpMethods.Initialize) => McpErrorMessages.SessionAlreadyInitialized,
            (true, false, var requestedMethod) when !IsAllowedBeforeReady(requestedMethod) => McpErrorMessages.SessionNotReady,
            _ => default
        };
    }

    private static bool TryValidateMeta(JsonElement parameters, out string error)
    {
        if (!parameters.TryGetProperty("_meta"u8, out var meta) || meta is { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null })
        {
            error = string.Empty;
            return true;
        }

        return McpMetaKeyValidator.TryValidateObjectKeys(meta, out error);
    }

    private static bool IsMcpNotificationMethod(string method)
    {
        return method.StartsWith("notifications/", StringComparison.Ordinal);
    }

    private static bool IsReservedJsonRpcMethod(string method)
    {
        return method.StartsWith("rpc.", StringComparison.Ordinal);
    }

    private static bool IsAllowedBeforeInitialize(string method)
    {
        return method is McpMethods.Initialize or McpMethods.Ping or McpMethods.NotificationsCancelled;
    }

    private static bool IsAllowedBeforeReady(string method)
    {
        return method is McpMethods.NotificationsInitialized or McpMethods.NotificationsCancelled or McpMethods.Ping;
    }

    private static Fin<JsonRpcDispatchResult> Respond(JsonRpcResponse response)
    {
        return Fin.Succ<JsonRpcDispatchResult>(JsonRpcDispatchResult.Respond(response));
    }

    private static Fin<JsonRpcDispatchResult> NoResponse()
    {
        return Fin.Succ<JsonRpcDispatchResult>(JsonRpcDispatchResult.NoResponse);
    }

    private static JsonRpcRequestId GetResponseId(JsonRpcMessage message)
    {
        return message.HasId ? message.Id : JsonRpcRequestId.Missing;
    }

    private readonly struct RequestScopeRegistration
    {
        private RequestScopeRegistration(McpRequestExecutionScope scope, Error? error, bool isSuccess)
        {
            Scope = scope;
            Error = error;
            IsSuccess = isSuccess;
        }

        public McpRequestExecutionScope Scope { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static RequestScopeRegistration Success(McpRequestExecutionScope scope)
        {
            return new RequestScopeRegistration(scope, default, isSuccess: true);
        }

        public static RequestScopeRegistration Fail(Error error)
        {
            return new RequestScopeRegistration(default, error, isSuccess: false);
        }
    }

    private readonly struct HandlerOutcome
    {
        private HandlerOutcome(JsonElement payload, Error? error, bool isSuccess)
        {
            Payload = payload;
            Error = error;
            IsSuccess = isSuccess;
        }

        public JsonElement Payload { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static HandlerOutcome Success(JsonElement payload)
        {
            return new HandlerOutcome(payload, default, isSuccess: true);
        }

        public static HandlerOutcome Fail(Error error)
        {
            return new HandlerOutcome(default, error, isSuccess: false);
        }
    }
}
