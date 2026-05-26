using System.Text.Json;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Infrastructure.Mcp.JsonRpc;

public sealed class JsonRpcMessageParser : IJsonRpcMessageParser
{
    private static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    public Fin<JsonRpcMessage> Parse(ReadOnlyMemory<byte> json)
    {
        if (json.IsEmpty || IsWhiteSpace(json.Span))
        {
            return Fail("JSON-RPC message is required.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, DocumentOptions);
            var root = document.RootElement;

            if (root is { ValueKind: JsonValueKind.Array })
            {
                return Fail("JSON-RPC batch messages are not supported by this server.");
            }

            if (root is not { ValueKind: JsonValueKind.Object })
            {
                return Fail("JSON-RPC message must be an object.");
            }

            var jsonRpc = TryReadString(root, JsonRpcPropertyName);
            var method = TryReadString(root, MethodPropertyName);
            var hasResult = root.TryGetProperty(ResultPropertyName, out _);
            var hasError = root.TryGetProperty(ErrorPropertyName, out _);
            var hasMethod = method is { } methodValue && !string.IsNullOrWhiteSpace(methodValue);

            if ((hasResult, hasError) is (true, true))
            {
                return Fail("JSON-RPC response must not contain both result and error.");
            }

            if (hasMethod && (hasResult || hasError))
            {
                return Fail("JSON-RPC message must not contain both method and response fields.");
            }

            var id = JsonRpcRequestId.Missing;
            if (root.TryGetProperty(IdPropertyName, out var idElement) && !JsonRpcRequestId.TryFromElement(idElement, out id))
            {
                return Fail("MCP JSON-RPC id must be a string or integer.");
            }

            JsonElement? parameters = default;
            if (root.TryGetProperty(ParamsPropertyName, out var paramsElement))
            {
                parameters = paramsElement.Clone();
            }

            JsonElement? result = default;
            if (root.TryGetProperty(ResultPropertyName, out var resultElement))
            {
                result = resultElement.Clone();
            }

            JsonElement? error = default;
            if (root.TryGetProperty(ErrorPropertyName, out var errorElement))
            {
                error = errorElement.Clone();
            }

            return Fin.Succ<JsonRpcMessage>(new JsonRpcMessage(jsonRpc, method, id, parameters, result, error, hasResult, hasError));
        }
        catch (JsonException ex)
        {
            return Fail(ex.Message);
        }
    }

    private static ReadOnlySpan<byte> JsonRpcPropertyName => "jsonrpc"u8;

    private static ReadOnlySpan<byte> MethodPropertyName => "method"u8;

    private static ReadOnlySpan<byte> IdPropertyName => "id"u8;

    private static ReadOnlySpan<byte> ParamsPropertyName => "params"u8;

    private static ReadOnlySpan<byte> ResultPropertyName => "result"u8;

    private static ReadOnlySpan<byte> ErrorPropertyName => "error"u8;

    private static string? TryReadString(JsonElement root, ReadOnlySpan<byte> propertyName)
    {
        return root.TryGetProperty(propertyName, out var element) && element is { ValueKind: JsonValueKind.String }
            ? element.GetString()
            : default;
    }

    private static Fin<JsonRpcMessage> Fail(string message)
    {
        return Fin.Fail<JsonRpcMessage>(Error.New(message));
    }

    private static bool IsWhiteSpace(ReadOnlySpan<byte> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsWhiteSpace((char)value[i]))
            {
                return false;
            }
        }

        return true;
    }
}
