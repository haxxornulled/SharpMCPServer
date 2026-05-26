using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Client.Interfaces;
using MCPServer.Client.Internal;
using MCPServer.Client.Tasking;
using MCPServer.Domain.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPServer.Client.Stdio;

public sealed class StdioMcpClientSession : IMcpClientSession
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly ReadOnlyMemory<byte> NewLine = new byte[] { (byte)'\n' };
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(5);

    private readonly McpClientProcessOptions _options;
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly StreamReader _reader;
    private readonly ILogger<StdioMcpClientSession> _logger;
    private readonly Task _stderrPump;
    private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
    private readonly McpClientInboundMessageRouter _inboundRouter;
    private int _nextRequestId;
    private bool _initializedNotificationSent;
    private bool _disposed;

    private StdioMcpClientSession(
        McpClientProcessOptions options,
        Process process,
        ILogger<StdioMcpClientSession> logger)
    {
        _options = options;
        _process = process;
        _input = process.StandardInput.BaseStream;
        _output = process.StandardOutput.BaseStream;
        _reader = new StreamReader(_output, Utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
        _logger = logger;
        _stderrPump = PumpStderrAsync(process, logger);

        var taskRegistry = options.ClientTaskRegistry ?? new InMemoryMcpClientTaskRegistry();
        _inboundRouter = new McpClientInboundMessageRouter(
            options.SamplingRequestHandler,
            options.ElicitationRequestHandler,
            taskRegistry,
            options.TaskStatusObserver,
            WriteSuccessResponseAsync,
            WriteErrorResponseAsync,
            WriteNotificationAsync,
            logger);
    }

    public static ValueTask<Fin<StdioMcpClientSession>> StartAsync(
        McpClientProcessOptions options,
        ILogger<StdioMcpClientSession>? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(options.ServerExecutablePath))
            {
                return FailStart("ServerExecutablePath is required.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = options.ServerExecutablePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
                    ? Path.GetDirectoryName(options.ServerExecutablePath) ?? Environment.CurrentDirectory
                    : options.WorkingDirectory
            };

            foreach (var argument in options.ServerArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            foreach (var variable in options.EnvironmentVariables)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            if (!process.Start())
            {
                process.Dispose();
                return FailStart("Failed to start MCP server process.");
            }

            return new ValueTask<Fin<StdioMcpClientSession>>(Fin.Succ(
                new StdioMcpClientSession(options, process, logger ?? NullLogger<StdioMcpClientSession>.Instance)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return FailStart("MCP server process start was cancelled.");
        }
        catch (Exception ex)
        {
            return FailStart($"Failed to start MCP server process: {ex.Message}");
        }
    }

    public async ValueTask<Fin<InitializeResult>> InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var id = NextRequestId();
        await WriteRequestAsync(id, McpMethods.Initialize, WriteInitializeParams, cancellationToken).ConfigureAwait(false);
        var result = await ReadResponseResultAsync(id, cancellationToken).ConfigureAwait(false);
        if (result.IsFail)
        {
            return result.Match<Fin<InitializeResult>>(Succ: _ => throw new InvalidOperationException(), Fail: error => error);
        }

        var initializeResult = result.Match(
            Succ: value => Deserialize(value, McpJsonSerializerContext.Default.InitializeResult),
            Fail: _ => throw new InvalidOperationException());

        if (initializeResult.IsFail)
        {
            return initializeResult;
        }

        var negotiatedProtocolVersion = initializeResult.Match(
            Succ: value => value.ProtocolVersion,
            Fail: _ => throw new InvalidOperationException());
        if (!McpProtocolVersions.IsSupported(negotiatedProtocolVersion))
        {
            return Fin.Fail<InitializeResult>(LanguageExt.Common.Error.New($"The MCP server negotiated unsupported protocol version '{negotiatedProtocolVersion}'."));
        }

        await SendInitializedNotificationAsync(cancellationToken).ConfigureAwait(false);
        return initializeResult;
    }

    public async ValueTask<Fin<ToolsListResult>> ListToolsAsync(string? cursor, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var id = NextRequestId();
        await WriteRequestAsync(id, McpMethods.ToolsList, writer => WriteCursorParams(writer, cursor), cancellationToken).ConfigureAwait(false);
        var result = await ReadResponseResultAsync(id, cancellationToken).ConfigureAwait(false);
        return result.Match(
            Succ: value => Deserialize(value, McpJsonSerializerContext.Default.ToolsListResult),
            Fail: error => Fin.Fail<ToolsListResult>(error));
    }

    public async ValueTask<Fin<ToolCallResult>> CallToolAsync(string name, JsonElement? arguments, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var id = NextRequestId();
        await WriteRequestAsync(id, McpMethods.ToolsCall, writer => WriteToolCallParams(writer, name, arguments), cancellationToken).ConfigureAwait(false);
        var result = await ReadResponseResultAsync(id, cancellationToken).ConfigureAwait(false);
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
        try
        {
            await CloseInputAsync().ConfigureAwait(false);
            _inboundRouter.Dispose();
            _reader.Dispose();
            await WaitForProcessExitAsync().ConfigureAwait(false);
        }
        finally
        {
            _process.Dispose();
        }

        try
        {
            await _stderrPump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private async ValueTask CloseInputAsync()
    {
        try
        {
            await _input.DisposeAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
    }

    private async ValueTask WaitForProcessExitAsync()
    {
        if (_process.HasExited)
        {
            return;
        }

        using var gracefulShutdownCts = new CancellationTokenSource(ShutdownGracePeriod);
        try
        {
            await _process.WaitForExitAsync(gracefulShutdownCts.Token).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private int NextRequestId()
    {
        return Interlocked.Increment(ref _nextRequestId);
    }

    private async ValueTask SendInitializedNotificationAsync(CancellationToken cancellationToken)
    {
        if (_initializedNotificationSent)
        {
            return;
        }

        _initializedNotificationSent = true;
        await WriteNotificationAsync(McpMethods.NotificationsInitialized, CreateEmptyObject(), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteRequestAsync(int id, string method, Action<Utf8JsonWriter>? writeParams, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(capacity: 1024);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false }))
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
            writer.Flush();
        }

        await WriteFrameAsync(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteNotificationAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(capacity: 256);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            parameters.WriteTo(writer);
            writer.WriteEndObject();
            writer.Flush();
        }

        await WriteFrameAsync(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteSuccessResponseAsync(string method, JsonElement id, JsonElement result, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(capacity: 512);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WritePropertyName("result");
            result.WriteTo(writer);
            writer.WriteEndObject();
            writer.Flush();
        }

        await WriteFrameAsync(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteErrorResponseAsync(string method, JsonElement id, int code, string message, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(capacity: 512);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false }))
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
            writer.Flush();
        }

        await WriteFrameAsync(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        if (ContainsLineBreak(frame.Span))
        {
            throw new McpClientProtocolException("Outgoing MCP stdio frame contained an embedded newline.");
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _input.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await _input.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async ValueTask<Fin<JsonElement>> ReadResponseResultAsync(int expectedId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                var exitSuffix = _process.HasExited
                    ? $" Server process exited with code {_process.ExitCode.ToString(CultureInfo.InvariantCulture)}."
                    : string.Empty;
                return Fin.Fail<JsonElement>(Error.New($"MCP server closed stdout before response {expectedId.ToString(CultureInfo.InvariantCulture)} was received.{exitSuffix}"));
            }

            if (line.Length > _options.MaxInputFrameBytes)
            {
                return Fin.Fail<JsonElement>(Error.New($"MCP response frame exceeded {_options.MaxInputFrameBytes.ToString(CultureInfo.InvariantCulture)} byte(s)."));
            }

            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(line);
                root = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                return Fin.Fail<JsonElement>(Error.New($"MCP server emitted invalid JSON on stdout: {ex.Message}"));
            }

            if (await _inboundRouter.TryHandleAsync(root, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (!root.TryGetProperty("id"u8, out var idElement) || !idElement.TryGetInt32(out var actualId))
            {
                return Fin.Fail<JsonElement>(Error.New("MCP response is missing an integer id."));
            }

            if (actualId != expectedId)
            {
                return Fin.Fail<JsonElement>(Error.New($"Received MCP response id {actualId.ToString(CultureInfo.InvariantCulture)} while waiting for {expectedId.ToString(CultureInfo.InvariantCulture)}. The simple stdio client is sequential and does not support pipelined requests."));
            }

            if (root.TryGetProperty("error"u8, out var errorElement))
            {
                return Fin.Fail<JsonElement>(Error.New(ReadJsonRpcErrorMessage(errorElement)));
            }

            if (!root.TryGetProperty("result"u8, out var resultElement))
            {
                return Fin.Fail<JsonElement>(Error.New("MCP response is missing result or error."));
            }

            return Fin.Succ(resultElement.Clone());
        }
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

    private static void WriteCursorParams(Utf8JsonWriter writer, string? cursor)
    {
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            writer.WriteString("cursor", cursor);
        }

        writer.WriteEndObject();
    }

    private static void WriteToolCallParams(Utf8JsonWriter writer, string name, JsonElement? arguments)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        if (arguments is { } suppliedArguments)
        {
            writer.WritePropertyName("arguments");
            suppliedArguments.WriteTo(writer);
        }

        writer.WriteEndObject();
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

    private static string ReadJsonRpcErrorMessage(JsonElement errorElement)
    {
        if (errorElement.ValueKind != JsonValueKind.Object)
        {
            return "MCP server returned a malformed JSON-RPC error.";
        }

        var code = errorElement.TryGetProperty("code"u8, out var codeElement) && codeElement.TryGetInt32(out var codeValue)
            ? codeValue.ToString(CultureInfo.InvariantCulture)
            : "unknown";
        var message = errorElement.TryGetProperty("message"u8, out var messageElement) && messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString() ?? "Unknown MCP error."
            : "Unknown MCP error.";

        return $"MCP JSON-RPC error {code}: {message}";
    }

    private static JsonElement CreateEmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static bool ContainsLineBreak(ReadOnlySpan<byte> frame)
    {
        for (var i = 0; i < frame.Length; i++)
        {
            if (frame[i] is (byte)'\n' or (byte)'\r')
            {
                return true;
            }
        }

        return false;
    }

    private static async Task PumpStderrAsync(Process process, ILogger logger)
    {
        try
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                logger.LogDebug("[MCP STDERR] {Line}", line);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static ValueTask<Fin<StdioMcpClientSession>> FailStart(string message)
    {
        return new ValueTask<Fin<StdioMcpClientSession>>(Fin.Fail<StdioMcpClientSession>(Error.New(message)));
    }
}
