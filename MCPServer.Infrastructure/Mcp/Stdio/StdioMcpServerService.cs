using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Application.Mcp;
using MCPServer.Domain.Mcp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MCPServer.Infrastructure.Mcp.Stdio;

public sealed class StdioMcpServerService : BackgroundService
{
    private readonly IMcpRequestDispatcher _dispatcher;
    private readonly IJsonRpcMessageParser _parser;
    private readonly IJsonRpcResponseSerializer _serializer;
    private readonly StdioMcpTransportOptions _options;
    private readonly ILogger<StdioMcpServerService> _logger;
    private readonly IStdioMcpClientFeatureTransport _clientFeatureTransport;

    public StdioMcpServerService(
        IMcpRequestDispatcher dispatcher,
        IJsonRpcMessageParser parser,
        IJsonRpcResponseSerializer serializer,
        StdioMcpTransportOptions options,
        ILogger<StdioMcpServerService> logger,
        IStdioMcpClientFeatureTransport clientFeatureTransport)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clientFeatureTransport);

        _dispatcher = dispatcher;
        _parser = parser;
        _serializer = serializer;
        _options = options;
        _logger = logger;
        _clientFeatureTransport = clientFeatureTransport;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("MCP stdio transport is disabled.");
            return;
        }

        _logger.LogInformation("MCP stdio transport started");

        await using var session = StdioMcpTransportSession.Open(_options);
        _clientFeatureTransport.Attach(session.Output, _serializer);

        while (!stoppingToken.IsCancellationRequested)
        {
            var readResult = await session.ReadFrameAsync(_options.MaxInputFrameBytes, stoppingToken).ConfigureAwait(false);
            if (readResult.IsEndOfInput)
            {
                _logger.LogInformation("MCP stdin closed; stopping stdio transport");
                break;
            }

            JsonRpcDispatchResult dispatch;
            if (readResult.IsInvalidFrame)
            {
                dispatch = JsonRpcDispatchResult.Respond(JsonRpcResponse.Failure(
                    JsonRpcRequestId.Missing,
                    JsonRpcErrorCodes.ParseError,
                    "MCP stdio messages must be newline-delimited and must not contain embedded newlines."));
            }
            else if (readResult.IsTooLarge)
            {
                dispatch = JsonRpcDispatchResult.Respond(JsonRpcResponse.Failure(
                    JsonRpcRequestId.Missing,
                    JsonRpcErrorCodes.InvalidRequest,
                    "JSON-RPC message exceeded the configured maximum input size."));
            }
            else if (readResult.Frame is { } frame)
            {
                using (frame)
                {
                    if (frame.Length == 0)
                    {
                        dispatch = JsonRpcDispatchResult.Respond(JsonRpcResponse.Failure(
                            JsonRpcRequestId.Missing,
                            JsonRpcErrorCodes.ParseError,
                            "MCP stdio frame was empty."));
                    }
                    else
                    {
                        dispatch = await ProcessFrameAsync(frame.Memory, stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                continue;
            }

            if (!dispatch.HasResponse)
            {
                continue;
            }

            await _serializer.WriteAsync(session.Output, dispatch.Response, stoppingToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<JsonRpcDispatchResult> ProcessFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        var parsed = _parser.Parse(frame);
        var parseOutcome = parsed.Match(
            Succ: static message => ParseOutcome.Success(message),
            Fail: static error => ParseOutcome.Fail(error));

        if (parseOutcome is not { IsSuccess: true })
        {
            var parseErrorMessage = parseOutcome.Error is { } parseError
                ? parseError.Message
                : "Parse error.";
            _logger.LogWarning("Failed to parse JSON-RPC message: {ErrorMessage}", parseErrorMessage);
            return JsonRpcDispatchResult.Respond(JsonRpcResponse.Failure(JsonRpcRequestId.Missing, JsonRpcErrorCodes.ParseError, "Parse error."));
        }

        try
        {
            if (parseOutcome.Message.IsResponse)
            {
                if (!_clientFeatureTransport.TryHandleResponse(parseOutcome.Message))
                {
                    _logger.LogWarning("Ignoring unmatched outbound MCP response from client.");
                }

                return JsonRpcDispatchResult.NoResponse;
            }

            var dispatched = await _dispatcher.DispatchAsync(parseOutcome.Message, cancellationToken).ConfigureAwait(false);
            var dispatchOutcome = dispatched.Match(
                Succ: static result => DispatchOutcome.Success(result),
                Fail: static error => DispatchOutcome.Fail(error));

            if (dispatchOutcome.IsSuccess)
            {
                return dispatchOutcome.Result;
            }

            var dispatchErrorMessage = dispatchOutcome.Error is { } dispatchError
                ? dispatchError.Message
                : "Internal MCP server error.";
            _logger.LogError("Failed to dispatch JSON-RPC message: {ErrorMessage}", dispatchErrorMessage);
            return JsonRpcDispatchResult.Respond(JsonRpcResponse.Failure(JsonRpcRequestId.Missing, JsonRpcErrorCodes.InternalError, "Internal MCP server error."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process JSON-RPC message");
            return JsonRpcDispatchResult.Respond(JsonRpcResponse.Failure(JsonRpcRequestId.Missing, JsonRpcErrorCodes.InternalError, "Internal MCP server error."));
        }
    }

    private readonly struct ParseOutcome
    {
        private ParseOutcome(JsonRpcMessage message, Error? error, bool isSuccess)
        {
            Message = message;
            Error = error;
            IsSuccess = isSuccess;
        }

        public JsonRpcMessage Message { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static ParseOutcome Success(JsonRpcMessage message)
        {
            return new ParseOutcome(message, default, isSuccess: true);
        }

        public static ParseOutcome Fail(Error error)
        {
            return new ParseOutcome(default, error, isSuccess: false);
        }
    }

    private readonly struct DispatchOutcome
    {
        private DispatchOutcome(JsonRpcDispatchResult result, Error? error, bool isSuccess)
        {
            Result = result;
            Error = error;
            IsSuccess = isSuccess;
        }

        public JsonRpcDispatchResult Result { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static DispatchOutcome Success(JsonRpcDispatchResult result)
        {
            return new DispatchOutcome(result, default, isSuccess: true);
        }

        public static DispatchOutcome Fail(Error error)
        {
            return new DispatchOutcome(default, error, isSuccess: false);
        }
    }
}
