using Autofac;
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
    private readonly ILifetimeScope _lifetimeScope;
    private readonly StdioMcpTransportOptions _options;
    private readonly ILogger<StdioMcpServerService> _logger;
    private readonly SemaphoreSlim _outputWriteLock = new SemaphoreSlim(1, 1);

    public StdioMcpServerService(
        ILifetimeScope lifetimeScope,
        StdioMcpTransportOptions options,
        ILogger<StdioMcpServerService> logger)
    {
        ArgumentNullException.ThrowIfNull(lifetimeScope);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _lifetimeScope = lifetimeScope;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("MCP stdio transport is disabled.");
            return;
        }

        _logger.LogInformation("MCP stdio transport started");

        using var sessionScope = _lifetimeScope.BeginLifetimeScope(McpLifetimeScopeTags.Session);
        var dispatcher = sessionScope.Resolve<IMcpRequestDispatcher>();
        var parser = sessionScope.Resolve<IJsonRpcMessageParser>();
        var serializer = sessionScope.Resolve<IJsonRpcResponseSerializer>();
        var clientFeatureTransport = sessionScope.Resolve<IStdioMcpClientFeatureTransport>();

        await using var session = StdioMcpTransportSession.Open(_options);
        clientFeatureTransport.Attach(session.Output, serializer, _outputWriteLock);

        var activeRequestTasks = new List<Task>();
        using var transportCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var readResult = await session.ReadFrameAsync(_options.MaxInputFrameBytes, transportCts.Token).ConfigureAwait(false);
                if (readResult.IsEndOfInput)
                {
                    _logger.LogInformation("MCP stdin closed; stopping stdio transport");
                    break;
                }

                if (readResult.IsInvalidFrame)
                {
                    activeRequestTasks.Add(WriteParseErrorAsync(
                        session.Output,
                        serializer,
                        JsonRpcErrorCodes.ParseError,
                        "MCP stdio messages must be newline-delimited and must not contain embedded newlines.",
                        transportCts.Token));
                    continue;
                }

                if (readResult.IsTooLarge)
                {
                    activeRequestTasks.Add(WriteParseErrorAsync(
                        session.Output,
                        serializer,
                        JsonRpcErrorCodes.InvalidRequest,
                        "JSON-RPC message exceeded the configured maximum input size.",
                        transportCts.Token));
                    continue;
                }

                if (readResult.Frame is not { } frame)
                {
                    continue;
                }

                using (frame)
                {
                    if (frame.Length == 0)
                    {
                        activeRequestTasks.Add(WriteParseErrorAsync(
                            session.Output,
                            serializer,
                            JsonRpcErrorCodes.ParseError,
                            "MCP stdio frame was empty.",
                            transportCts.Token));
                        continue;
                    }

                    var frameBytes = frame.Memory.ToArray();
                    activeRequestTasks.Add(HandleFrameAsync(frameBytes, session.Output, parser, dispatcher, clientFeatureTransport, serializer, transportCts.Token));
                }
            }
        }
        finally
        {
            transportCts.Cancel();

            try
            {
                if (activeRequestTasks.Count > 0)
                {
                    await Task.WhenAll(activeRequestTasks).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (transportCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "One or more MCP stdio request handlers failed during shutdown.");
            }
        }
    }

    private async Task HandleFrameAsync(
        ReadOnlyMemory<byte> frame,
        Stream output,
        IJsonRpcMessageParser parser,
        IMcpRequestDispatcher dispatcher,
        IStdioMcpClientFeatureTransport clientFeatureTransport,
        IJsonRpcResponseSerializer serializer,
        CancellationToken cancellationToken)
    {
        try
        {
            var parsed = parser.Parse(frame);
            var parseOutcome = parsed.Match(
                Succ: static message => ParseOutcome.Success(message),
                Fail: static error => ParseOutcome.Fail(error));

            if (parseOutcome is not { IsSuccess: true })
            {
                var parseErrorMessage = parseOutcome.Error is { } parseError
                    ? parseError.Message
                    : "Parse error.";
                _logger.LogWarning("Failed to parse JSON-RPC message: {ErrorMessage}", parseErrorMessage);
                await WriteResponseAsync(
                    output,
                    serializer,
                    JsonRpcResponse.Failure(
                        JsonRpcRequestId.Missing,
                        JsonRpcErrorCodes.ParseError,
                        "Parse error."),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (parseOutcome.Message.IsResponse)
            {
                if (!clientFeatureTransport.TryHandleResponse(parseOutcome.Message))
                {
                    _logger.LogWarning("Ignoring unmatched outbound MCP response from client.");
                }
                return;
            }

            var dispatched = await dispatcher.DispatchAsync(parseOutcome.Message, cancellationToken).ConfigureAwait(false);
            var dispatchOutcome = dispatched.Match(
                Succ: static result => DispatchOutcome.Success(result),
                Fail: static error => DispatchOutcome.Fail(error));

            if (dispatchOutcome.IsSuccess)
            {
                if (dispatchOutcome.Result.HasResponse)
                {
                    await WriteResponseAsync(output, serializer, dispatchOutcome.Result.Response, cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            var dispatchErrorMessage = dispatchOutcome.Error is { } dispatchError
                ? dispatchError.Message
                : "Internal MCP server error.";
            _logger.LogError("Failed to dispatch JSON-RPC message: {ErrorMessage}", dispatchErrorMessage);
            await WriteResponseAsync(
                output,
                serializer,
                JsonRpcResponse.Failure(JsonRpcRequestId.Missing, JsonRpcErrorCodes.InternalError, "Internal MCP server error."),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process JSON-RPC message");
        }
    }

    private async Task WriteParseErrorAsync(Stream output, IJsonRpcResponseSerializer serializer, int errorCode, string message, CancellationToken cancellationToken)
    {
        try
        {
            await WriteResponseAsync(
                output,
                serializer,
                JsonRpcResponse.Failure(JsonRpcRequestId.Missing, errorCode, message),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write MCP stdio parse error response.");
        }
    }

    private async ValueTask WriteResponseAsync(Stream output, IJsonRpcResponseSerializer serializer, JsonRpcResponse response, CancellationToken cancellationToken)
    {
        await _outputWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await serializer.WriteAsync(output, response, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _outputWriteLock.Release();
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
