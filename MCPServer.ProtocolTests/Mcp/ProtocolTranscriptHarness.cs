using System.Text.Json;
using System.Text;
using Autofac;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Application.Mcp;
using MCPServer.Application;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace MCPServer.ProtocolTests.Mcp;

internal sealed class ProtocolTranscriptHarness : IDisposable
{
    private readonly IContainer _container;
    private readonly IJsonRpcMessageParser _parser;
    private readonly IMcpRequestDispatcher _dispatcher;
    private readonly IJsonRpcResponseSerializer _serializer;
    private bool _disposed;

    private ProtocolTranscriptHarness(IContainer container)
    {
        _container = container;
        _parser = _container.Resolve<IJsonRpcMessageParser>();
        _dispatcher = _container.Resolve<IMcpRequestDispatcher>();
        _serializer = _container.Resolve<IJsonRpcResponseSerializer>();
    }

    public static ProtocolTranscriptHarness Create()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule(new ApplicationModule());
        builder.RegisterModule(new InfrastructureModule());
        return new ProtocolTranscriptHarness(builder.Build());
    }

    public async ValueTask<ProtocolTranscript> SendAsync(params string[] frames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await using var output = new MemoryStream();

        for (var i = 0; i < frames.Length; i++)
        {
            await ProcessFrameAsync(Encoding.UTF8.GetBytes(frames[i]), output, CancellationToken.None).ConfigureAwait(false);
        }

        return ProtocolTranscript.FromUtf8(output.ToArray());
    }

    public async ValueTask<ProtocolTranscript> SendRawAsync(params byte[][] frames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await using var output = new MemoryStream();

        for (var i = 0; i < frames.Length; i++)
        {
            await ProcessFrameAsync(frames[i], output, CancellationToken.None).ConfigureAwait(false);
        }

        return ProtocolTranscript.FromUtf8(output.ToArray());
    }

    private async ValueTask ProcessFrameAsync(ReadOnlyMemory<byte> frame, Stream output, CancellationToken cancellationToken)
    {
        var parsed = _parser.Parse(frame);
        var parseOutcome = parsed.Match(
            Succ: static message => ParseOutcome.Success(message),
            Fail: static error => ParseOutcome.Fail(error));

        if (!parseOutcome.IsSuccess)
        {
            var response = JsonRpcResponse.Failure(
                JsonRpcRequestId.Missing,
                JsonRpcErrorCodes.ParseError,
                "Parse error.");

            await _serializer.WriteAsync(output, response, cancellationToken).ConfigureAwait(false);
            return;
        }

        var dispatched = await _dispatcher.DispatchAsync(parseOutcome.Message, cancellationToken).ConfigureAwait(false);
        var dispatchOutcome = dispatched.Match(
            Succ: static result => DispatchOutcome.Success(result),
            Fail: static error => DispatchOutcome.Fail(error));

        if (!dispatchOutcome.IsSuccess)
        {
            var response = JsonRpcResponse.Failure(
                JsonRpcRequestId.Missing,
                JsonRpcErrorCodes.InternalError,
                dispatchOutcome.Error is { } dispatchError ? dispatchError.Message : "Internal MCP server error.");

            await _serializer.WriteAsync(output, response, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (dispatchOutcome.Result.HasResponse)
        {
            await _serializer.WriteAsync(output, dispatchOutcome.Result.Response, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _container.Dispose();
        _disposed = true;
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

internal sealed class ProtocolTranscript : IDisposable
{
    private readonly JsonDocument[] _documents;
    private bool _disposed;

    private ProtocolTranscript(JsonDocument[] documents, string rawUtf8)
    {
        _documents = documents;
        RawUtf8 = rawUtf8;
    }

    public string RawUtf8 { get; }

    public int Count => _documents.Length;

    public JsonElement this[int index] => _documents[index].RootElement;

    public static ProtocolTranscript FromUtf8(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return new ProtocolTranscript(Array.Empty<JsonDocument>(), string.Empty);
        }

        var raw = Encoding.UTF8.GetString(bytes);
        var lines = raw.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var documents = new JsonDocument[lines.Length];

        try
        {
            for (var i = 0; i < lines.Length; i++)
            {
                documents[i] = JsonDocument.Parse(lines[i]);
            }
        }
        catch
        {
            for (var i = 0; i < documents.Length; i++)
            {
                documents[i]?.Dispose();
            }

            throw;
        }

        return new ProtocolTranscript(documents, raw);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (var i = 0; i < _documents.Length; i++)
        {
            _documents[i].Dispose();
        }

        _disposed = true;
    }
}
