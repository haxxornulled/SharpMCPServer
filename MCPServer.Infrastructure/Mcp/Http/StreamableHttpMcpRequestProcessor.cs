using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure.Mcp.JsonRpc;
using Microsoft.Extensions.Logging;

namespace MCPServer.Infrastructure.Mcp.Http;

public sealed class StreamableHttpMcpRequestProcessor : IStreamableHttpMcpRequestProcessor
{
    private const string JsonContentType = "application/json";
    private const string TextEventStreamContentType = "text/event-stream";
    private const string AllowHeaderValue = "POST";

    private readonly IMcpRequestDispatcher _dispatcher;
    private readonly IJsonRpcMessageParser _parser;
    private readonly IJsonRpcResponseSerializer _serializer;
    private readonly IMcpToolRegistry _toolRegistry;
    private readonly IStreamableHttpMcpSessionTransport _sessionTransport;
    private readonly ILogger<StreamableHttpMcpRequestProcessor> _logger;

    public StreamableHttpMcpRequestProcessor(
        IMcpRequestDispatcher dispatcher,
        IJsonRpcMessageParser parser,
        IJsonRpcResponseSerializer serializer,
        IMcpToolRegistry toolRegistry,
        IStreamableHttpMcpSessionTransport sessionTransport,
        ILogger<StreamableHttpMcpRequestProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(sessionTransport);
        ArgumentNullException.ThrowIfNull(logger);

        _dispatcher = dispatcher;
        _parser = parser;
        _serializer = serializer;
        _toolRegistry = toolRegistry;
        _sessionTransport = sessionTransport;
        _logger = logger;
    }

    public async ValueTask<StreamableHttpMcpResponse> ProcessAsync(StreamableHttpMcpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidateOrigin(request, out var originError))
        {
            return Forbidden(originError);
        }

        if (string.Equals(request.Method, HttpMethod.Get.Method, StringComparison.OrdinalIgnoreCase))
        {
            return MethodNotAllowed();
        }

        if (!string.Equals(request.Method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase))
        {
            return MethodNotAllowed();
        }

        if (!TryValidateRequestHeaders(request, out var requestHeaderError))
        {
            return BadRequest(JsonRpcRequestId.Missing, requestHeaderError, JsonRpcErrorCodes.InvalidRequest);
        }

        var parsed = _parser.Parse(request.Body);
        var parseOutcome = parsed.Match(
            Succ: static message => ParseOutcome.Success(message),
            Fail: static error => ParseOutcome.Fail(error));

        if (parseOutcome is not { IsSuccess: true })
        {
            var parseErrorMessage = parseOutcome.Error is { } parseError
                ? parseError.Message
                : "Parse error.";
            _logger.LogWarning("Failed to parse MCP HTTP message: {ErrorMessage}", parseErrorMessage);
            return ParseError(parseErrorMessage);
        }

        var message = parseOutcome.Message;
        var isInitialize = string.Equals(message.Method, McpMethods.Initialize, StringComparison.Ordinal);

        if (!_sessionTransport.TryValidateSessionRequest(request, isInitialize, out var sessionStatusCode, out var sessionError))
        {
            return ErrorResponse(
                sessionStatusCode,
                JsonRpcRequestId.Missing,
                JsonRpcErrorCodes.InvalidRequest,
                sessionError);
        }

        if (!TryValidateProtocolVersion(request, isInitialize, out var protocolVersionError))
        {
            return BadRequest(JsonRpcRequestId.Missing, protocolVersionError, JsonRpcErrorCodes.InvalidRequest);
        }

        if (message.IsResponse)
        {
            if (!_sessionTransport.TryHandleResponse(message))
            {
                _logger.LogWarning("Received an unmatched outbound MCP response over Streamable HTTP.");
            }

            return Accepted();
        }

        if (!TryValidateMessageHeaders(request, message, out var messageHeaderError))
        {
            return BadRequest(message.HasId ? message.Id : JsonRpcRequestId.Missing, messageHeaderError, GetErrorCodeForValidation(message));
        }

        try
        {
            if (isInitialize)
            {
                _sessionTransport.StartSession();
            }

            var dispatched = await _dispatcher.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
            var dispatchOutcome = dispatched.Match(
                Succ: static result => DispatchOutcome.Success(result),
                Fail: static error => DispatchOutcome.Fail(error));

            if (dispatchOutcome is not { IsSuccess: true })
            {
                var messageText = dispatchOutcome.Error is { } dispatchError
                    ? dispatchError.Message
                    : "Internal MCP server error.";
                _logger.LogError("Failed to dispatch MCP HTTP message: {ErrorMessage}", messageText);

                if (isInitialize)
                {
                    _sessionTransport.TerminateSession();
                }

                return InternalServerError(message.HasId ? message.Id : JsonRpcRequestId.Missing, messageText);
            }

            if (!dispatchOutcome.Result.HasResponse)
            {
                return Accepted();
            }

            var response = await SerializeJsonRpcResponseAsync(dispatchOutcome.Result.Response, HttpStatusCode.OK, cancellationToken).ConfigureAwait(false);
            if (isInitialize && _sessionTransport.SessionId is { } sessionId)
            {
                response.Headers[StreamableHttpMcpHeaderNames.SessionId] = sessionId;
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (isInitialize)
            {
                _sessionTransport.TerminateSession();
            }

            _logger.LogError(ex, "Unhandled exception while processing MCP HTTP message");
            return InternalServerError(JsonRpcRequestId.Missing, "Internal MCP server error.");
        }
    }

    private static StreamableHttpMcpResponse Accepted()
    {
        return StreamableHttpMcpResponse.Empty(HttpStatusCode.Accepted);
    }

    private static StreamableHttpMcpResponse MethodNotAllowed()
    {
        return new StreamableHttpMcpResponse
        {
            StatusCode = HttpStatusCode.MethodNotAllowed,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Allow"] = AllowHeaderValue
            }
        };
    }

    private static StreamableHttpMcpResponse Forbidden(string message)
    {
        return ErrorResponse(HttpStatusCode.Forbidden, JsonRpcRequestId.Missing, JsonRpcErrorCodes.InvalidRequest, message);
    }

    private static StreamableHttpMcpResponse BadRequest(JsonRpcRequestId id, string message, int errorCode)
    {
        return ErrorResponse(HttpStatusCode.BadRequest, id, errorCode, message);
    }

    private static StreamableHttpMcpResponse ParseError(string message)
    {
        return ErrorResponse(HttpStatusCode.BadRequest, JsonRpcRequestId.Missing, JsonRpcErrorCodes.ParseError, message);
    }

    private static StreamableHttpMcpResponse InternalServerError(JsonRpcRequestId id, string message)
    {
        return ErrorResponse(HttpStatusCode.InternalServerError, id, JsonRpcErrorCodes.InternalError, message);
    }

    private static StreamableHttpMcpResponse ErrorResponse(HttpStatusCode statusCode, JsonRpcRequestId id, int errorCode, string message)
    {
        var response = JsonRpcResponse.Failure(id, errorCode, message);
        return new StreamableHttpMcpResponse
        {
            StatusCode = statusCode,
            ContentType = JsonContentType,
            Body = SerializeJsonRpcResponse(response)
        };
    }

    private static byte[] SerializeJsonRpcResponse(JsonRpcResponse response)
    {
        using var stream = new MemoryStream();
        var serializer = new JsonRpcResponseSerializer(new JsonRpcSerializationOptions
        {
            ValidateNoEmbeddedNewlines = false
        });

        serializer.WriteAsync(stream, response, CancellationToken.None).GetAwaiter().GetResult();
        return stream.ToArray();
    }

    private async ValueTask<StreamableHttpMcpResponse> SerializeJsonRpcResponseAsync(JsonRpcResponse response, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream();
        await _serializer.WriteAsync(stream, response, cancellationToken).ConfigureAwait(false);

        return new StreamableHttpMcpResponse
        {
            StatusCode = statusCode,
            ContentType = JsonContentType,
            Body = stream.ToArray()
        };
    }

    private bool TryValidateOrigin(StreamableHttpMcpRequest request, out string error)
    {
        var originHeader = request.GetHeader(StreamableHttpMcpHeaderNames.Origin);
        if (string.IsNullOrWhiteSpace(originHeader))
        {
            error = string.Empty;
            return true;
        }

        if (!Uri.TryCreate(originHeader, UriKind.Absolute, out var originUri) ||
            !originUri.IsLoopback ||
            !string.Equals(originUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(originUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Invalid Origin header.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryValidateRequestHeaders(StreamableHttpMcpRequest request, out string error)
    {
        if (!HasMediaType(request.GetHeader(StreamableHttpMcpHeaderNames.Accept), JsonContentType) ||
            !HasMediaType(request.GetHeader(StreamableHttpMcpHeaderNames.Accept), TextEventStreamContentType))
        {
            error = "Accept header must include application/json and text/event-stream.";
            return false;
        }

        if (!HasMediaType(request.GetHeader(StreamableHttpMcpHeaderNames.ContentType), JsonContentType))
        {
            error = "Content-Type header must be application/json.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryValidateProtocolVersion(StreamableHttpMcpRequest request, bool isInitialize, out string error)
    {
        var protocolVersion = request.GetHeader(StreamableHttpMcpHeaderNames.ProtocolVersion);
        if (string.IsNullOrWhiteSpace(protocolVersion))
        {
            if (isInitialize)
            {
                error = string.Empty;
                return true;
            }

            error = "Missing MCP-Protocol-Version header.";
            return false;
        }

        if (!McpProtocolVersions.IsSupported(protocolVersion))
        {
            error = $"Unsupported MCP protocol version '{protocolVersion}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryValidateMessageHeaders(StreamableHttpMcpRequest request, JsonRpcMessage message, out string error)
    {
        error = string.Empty;

        if (message.HasMethod)
        {
            var methodHeader = request.GetHeader(StreamableHttpMcpHeaderNames.Method);
            if (string.IsNullOrWhiteSpace(methodHeader))
            {
                error = "Mcp-Method header is required.";
                return false;
            }

            if (!string.Equals(methodHeader, message.Method, StringComparison.Ordinal))
            {
                error = "Mcp-Method header does not match the JSON-RPC method.";
                return false;
            }
        }

        if (message.Method is McpMethods.ToolsCall)
        {
            return TryValidateToolHeaders(request, message, out error);
        }

        if (message.Method is McpMethods.ToolsCall or McpMethods.ResourcesRead or McpMethods.PromptsGet)
        {
            if (!TryValidateMcpName(request, message, out error))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryValidateToolHeaders(StreamableHttpMcpRequest request, JsonRpcMessage message, out string error)
    {
        error = string.Empty;

        if (message.Params is not { ValueKind: JsonValueKind.Object } parameters ||
            !parameters.TryGetProperty("name"u8, out var toolNameElement) ||
            toolNameElement.ValueKind != JsonValueKind.String)
        {
            return true;
        }

        var toolName = toolNameElement.GetString();
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return true;
        }

        var toolResult = _toolRegistry.FindTool(toolName);
        if (toolResult.IsFail)
        {
            return true;
        }

        var tool = toolResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var bindings = GetToolHeaderBindings(tool.Descriptor);
        if (bindings.Length == 0)
        {
            return true;
        }

        foreach (var binding in bindings)
        {
            if (!parameters.TryGetProperty(binding.ParameterName, out var parameterValue) || parameterValue.ValueKind is JsonValueKind.Null)
            {
                continue;
            }

            var headerName = StreamableHttpMcpHeaderNames.ParamPrefix + binding.HeaderName;
            if (!request.Headers.TryGetValue(headerName, out var headerValue))
            {
                error = $"MCP header '{headerName}' is required for tool '{toolName}'.";
                return false;
            }

            if (!TryValidateHeaderValue(parameterValue, headerValue, out var validationError))
            {
                error = $"MCP header '{headerName}' is invalid: {validationError}";
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateMcpName(StreamableHttpMcpRequest request, JsonRpcMessage message, out string error)
    {
        error = string.Empty;

        var expectedName = message.Method switch
        {
            McpMethods.ToolsCall => TryReadStringProperty(message.Params, "name"u8),
            McpMethods.ResourcesRead => TryReadStringProperty(message.Params, "uri"u8),
            McpMethods.PromptsGet => TryReadStringProperty(message.Params, "name"u8),
            _ => default
        };

        if (string.IsNullOrWhiteSpace(expectedName))
        {
            return true;
        }

        var headerName = request.GetHeader(StreamableHttpMcpHeaderNames.Name);
        if (string.IsNullOrWhiteSpace(headerName))
        {
            error = "Mcp-Name header is required.";
            return false;
        }

        if (!string.Equals(headerName, expectedName, StringComparison.Ordinal))
        {
            error = "Mcp-Name header does not match the request body.";
            return false;
        }

        return true;
    }

    private static string? FindMethod(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return TryReadStringProperty(document.RootElement, "method"u8);
        }
        catch
        {
            return default;
        }
    }

    private static string? TryReadStringProperty(JsonElement? element, ReadOnlySpan<byte> propertyName)
    {
        if (element is not { } root || root.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        return root.TryGetProperty(propertyName, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()
            : default;
    }

    private static string? TryReadStringProperty(JsonElement element, ReadOnlySpan<byte> propertyName)
    {
        return element.TryGetProperty(propertyName, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()
            : default;
    }

    private static bool TryValidateHeaderValue(JsonElement value, string headerValue, out string error)
    {
        error = string.Empty;
        if (!TryGetExpectedHeaderValue(value, out var expectedValue))
        {
            error = $"'{value.ValueKind}' is not a supported MCP header value type.";
            return false;
        }

        if (!TryDecodeHeaderValue(headerValue, out var decodedHeaderValue))
        {
            error = "The header value could not be decoded.";
            return false;
        }

        if (!string.Equals(decodedHeaderValue, expectedValue, StringComparison.Ordinal))
        {
            error = "The header value does not match the request body.";
            return false;
        }

        return true;
    }

    private static bool TryGetExpectedHeaderValue(JsonElement value, out string expectedValue)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                expectedValue = value.GetString() ?? string.Empty;
                return true;
            case JsonValueKind.Number:
                expectedValue = FormatHeaderNumber(value);
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                expectedValue = value.GetBoolean() ? "true" : "false";
                return true;
            default:
                expectedValue = string.Empty;
                return false;
        }
    }

    private static string FormatHeaderNumber(JsonElement value)
    {
        if (value.TryGetInt64(out var longValue))
        {
            return longValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetDecimal(out var decimalValue))
        {
            return decimalValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetDouble(out var doubleValue))
        {
            return doubleValue.ToString(CultureInfo.InvariantCulture);
        }

        return value.GetRawText();
    }

    private static bool TryDecodeHeaderValue(string headerValue, out string decodedValue)
    {
        const string Prefix = "=?base64?";
        const string Suffix = "?=";

        if (headerValue.StartsWith(Prefix, StringComparison.Ordinal) && headerValue.EndsWith(Suffix, StringComparison.Ordinal))
        {
            var base64 = headerValue[Prefix.Length..^Suffix.Length];
            try
            {
                decodedValue = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                return true;
            }
            catch (FormatException)
            {
                decodedValue = string.Empty;
                return false;
            }
        }

        decodedValue = headerValue;
        return true;
    }

    private static bool HasMediaType(string? headerValue, string mediaType)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var values = headerValue.Split(',');
        for (var i = 0; i < values.Length; i++)
        {
            var token = values[i].Trim();
            var semicolonIndex = token.IndexOf(';');
            if (semicolonIndex >= 0)
            {
                token = token[..semicolonIndex].Trim();
            }

            if (string.Equals(token, mediaType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetErrorCodeForValidation(JsonRpcMessage message)
    {
        if (message.Method is McpMethods.ToolsCall or McpMethods.ResourcesRead or McpMethods.PromptsGet)
        {
            return JsonRpcErrorCodes.InvalidParams;
        }

        return JsonRpcErrorCodes.InvalidRequest;
    }

    private static ToolHeaderBinding[] GetToolHeaderBindings(McpToolDescriptor tool)
    {
        if (tool.InputSchema.ValueKind != JsonValueKind.Object ||
            !tool.InputSchema.TryGetProperty("properties"u8, out var propertiesElement) ||
            propertiesElement.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<ToolHeaderBinding>();
        }

        var bindings = new List<ToolHeaderBinding>();
        var headerNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in propertiesElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty("x-mcp-header"u8, out var headerNameElement) ||
                headerNameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var headerName = headerNameElement.GetString();
            if (string.IsNullOrWhiteSpace(headerName))
            {
                continue;
            }

            if (!headerNames.Add(headerName))
            {
                continue;
            }

            bindings.Add(new ToolHeaderBinding(property.Name, headerName));
        }

        return bindings.ToArray();
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

    private readonly record struct ToolHeaderBinding(string ParameterName, string HeaderName);
}
