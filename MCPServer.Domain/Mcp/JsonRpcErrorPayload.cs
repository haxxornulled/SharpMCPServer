using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public readonly struct JsonRpcErrorPayload
{
    public JsonRpcErrorPayload(int code, string message, JsonElement? data = default)
    {
        Code = code;
        Message = message;
        Data = data;
    }

    public int Code { get; }

    public string Message { get; }

    public JsonElement? Data { get; }

    public static JsonRpcErrorPayload Create(int code, string message, JsonElement? data = default)
    {
        return new JsonRpcErrorPayload(code, message, data);
    }
}
