using System.Text.Json;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Infrastructure.Mcp.JsonRpc;

public sealed class JsonRpcResponseSerializer : IJsonRpcResponseSerializer
{
    private const int MinimumInitialBufferBytes = 128;
    private const int MinimumMaxOutputFrameBytes = 1_024;

    private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions
    {
        Indented = false,
        SkipValidation = false
    };

    private static readonly JsonEncodedText JsonRpcProperty = JsonEncodedText.Encode("jsonrpc");
    private static readonly JsonEncodedText IdProperty = JsonEncodedText.Encode("id");
    private static readonly JsonEncodedText MethodProperty = JsonEncodedText.Encode("method");
    private static readonly JsonEncodedText ParamsProperty = JsonEncodedText.Encode("params");
    private static readonly JsonEncodedText ResultProperty = JsonEncodedText.Encode("result");
    private static readonly JsonEncodedText ErrorProperty = JsonEncodedText.Encode("error");
    private static readonly JsonEncodedText CodeProperty = JsonEncodedText.Encode("code");
    private static readonly JsonEncodedText MessageProperty = JsonEncodedText.Encode("message");
    private static readonly JsonEncodedText DataProperty = JsonEncodedText.Encode("data");
    private static readonly ReadOnlyMemory<byte> NewLine = new byte[] { (byte)'\n' };

    private readonly int _initialBufferBytes;
    private readonly int _maxOutputFrameBytes;
    private readonly bool _validateNoEmbeddedNewlines;
    private readonly bool _clearReturnedOutputBuffers;

    public JsonRpcResponseSerializer()
        : this(new JsonRpcSerializationOptions())
    {
    }

    public JsonRpcResponseSerializer(JsonRpcSerializationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _initialBufferBytes = Math.Max(MinimumInitialBufferBytes, options.InitialBufferBytes);
        _maxOutputFrameBytes = Math.Max(MinimumMaxOutputFrameBytes, options.MaxOutputFrameBytes);
        _validateNoEmbeddedNewlines = options.ValidateNoEmbeddedNewlines;
        _clearReturnedOutputBuffers = options.ClearReturnedOutputBuffers;
    }

    public async ValueTask WriteAsync(Stream output, JsonRpcResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);

        using var buffer = PooledByteBufferWriter.Rent(_initialBufferBytes, _clearReturnedOutputBuffers);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString(JsonRpcProperty, "2.0");

            if (response.Id.IsSpecified)
            {
                writer.WritePropertyName(IdProperty);
                response.Id.WriteTo(writer);
            }

            if (response.Error is { } error)
            {
                writer.WritePropertyName(ErrorProperty);
                WriteError(writer, error);
            }
            else
            {
                writer.WritePropertyName(ResultProperty);
                if (response.Result is { } result)
                {
                    result.WriteTo(writer);
                }
                else
                {
                    McpJsonElements.EmptyObject.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        await WriteFrameAsync(output, buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteNotificationAsync(Stream output, string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        using var buffer = PooledByteBufferWriter.Rent(_initialBufferBytes, _clearReturnedOutputBuffers);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString(JsonRpcProperty, "2.0");
            writer.WriteString(MethodProperty, method);

            if (parameters is { } payload)
            {
                writer.WritePropertyName(ParamsProperty);
                payload.WriteTo(writer);
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        await WriteFrameAsync(output, buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteFrameAsync(Stream output, ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        if (frame.Length > _maxOutputFrameBytes)
        {
            throw new InvalidOperationException($"Serialized MCP output frame exceeded the configured maximum of {_maxOutputFrameBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)} byte(s).");
        }

        if (_validateNoEmbeddedNewlines && ContainsLineBreak(frame.Span))
        {
            throw new InvalidOperationException("Serialized MCP stdio frame contained an embedded newline.");
        }

        await output.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool ContainsLineBreak(ReadOnlySpan<byte> frame)
    {
        for (var i = 0; i < frame.Length; i++)
        {
            var value = frame[i];
            if (value is (byte)'\n' or (byte)'\r')
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteError(Utf8JsonWriter writer, JsonRpcErrorPayload error)
    {
        writer.WriteStartObject();
        writer.WriteNumber(CodeProperty, error.Code);
        writer.WriteString(MessageProperty, error.Message);

        if (error.Data is { } data)
        {
            writer.WritePropertyName(DataProperty);
            data.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
