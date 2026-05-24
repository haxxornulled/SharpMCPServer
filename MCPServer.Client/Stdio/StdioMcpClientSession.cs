using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Client.Interfaces;
using MCPServer.Domain.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPServer.Client.Stdio;

public sealed class StdioMcpClientSession : IMcpClientSession
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly ReadOnlyMemory<byte> NewLine = new byte[] { (byte)'\n' };

    private readonly McpClientProcessOptions _options;
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly StreamReader _reader;
    private readonly ILogger<StdioMcpClientSession> _logger;
    private readonly Task _stderrPump;
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
    }

    public static ValueTask<Fin<StdioMcpClientSession>> StartAsync(
        McpClientProcessOptions options,
        ILogger<StdioMcpClientSession>? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(options.ServerExecutablePath))
        {
            return FailStart("ServerExecutablePath is required.");
        }

        if (!File.Exists(options.ServerExecutablePath))
        {
            return FailStart($"MCP server executable was not found: {options.ServerExecutablePath}");
        }

        try
        {
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

            return new ValueTask<Fin<StdioMcpClientSession>>(Fin.Succ<StdioMcpClientSession>(
                new StdioMcpClientSession(options, process, logger ?? NullLogger<StdioMcpClientSession>.Instance)));
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
            return result.Match<Fin<InitializeResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected initialize success while handling failure."),
                Fail: error => error);
        }

        var initializeResult = result.Match(
            Succ: value => Deserialize(value, McpJsonSerializerContext.Default.InitializeResult),
            Fail: _ => throw new InvalidOperationException("Unexpected initialize failure while handling success."));

        if (initializeResult.IsFail)
        {
            return initializeResult;
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
        _reader.Dispose();

        try
        {
            await _input.DisposeAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
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
        await WriteNotificationAsync(McpMethods.NotificationsInitialized, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteRequestAsync(
        int id,
        string method,
        Action<Utf8JsonWriter>? writeParams,
        CancellationToken cancellationToken)
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

    private async ValueTask WriteNotificationAsync(string method, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(capacity: 256);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", method);
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

        await _input.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await _input.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
        await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
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

            if (root.TryGetProperty("method"u8, out var methodElement) && methodElement.ValueKind == JsonValueKind.String)
            {
                _logger.LogDebug("Received MCP notification {Method}", methodElement.GetString());
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

            return Fin.Succ<JsonElement>(resultElement.Clone());
        }
    }

    private void WriteInitializeParams(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("protocolVersion", McpProtocolVersions.Current);
        writer.WritePropertyName("capabilities");
        writer.WriteStartObject();
        writer.WriteEndObject();
        writer.WritePropertyName("clientInfo");
        writer.WriteStartObject();
        writer.WriteString("name", _options.ClientName);
        writer.WriteString("title", _options.ClientTitle);
        writer.WriteString("version", _options.ClientVersion);
        writer.WriteEndObject();
        writer.WriteEndObject();
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
                : Fin.Succ<T>(value);
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
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                logger.LogDebug("MCP server stderr: {Line}", line);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static ValueTask<Fin<StdioMcpClientSession>> FailStart(string message)
    {
        return new ValueTask<Fin<StdioMcpClientSession>>(Fin.Fail<StdioMcpClientSession>(Error.New(message)));
    }
}
