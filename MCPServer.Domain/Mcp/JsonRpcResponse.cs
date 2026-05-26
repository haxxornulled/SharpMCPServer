using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public readonly struct JsonRpcResponse
{
    private JsonRpcResponse(JsonRpcRequestId id, JsonElement? result, JsonRpcErrorPayload? error)
    {
        Id = id;
        Result = result;
        Error = error;
    }

    public JsonRpcRequestId Id { get; }

    public JsonElement? Result { get; }

    public JsonRpcErrorPayload? Error { get; }

    public bool IsError => Error.HasValue;

    public static JsonRpcResponse Success(JsonRpcRequestId id, JsonElement result)
    {
        return new JsonRpcResponse(id, result, error: default);
    }

    public static JsonRpcResponse Failure(JsonRpcRequestId id, int code, string message, JsonElement? data = default)
    {
        return new JsonRpcResponse(id, result: default, JsonRpcErrorPayload.Create(code, message, data));
    }
}
