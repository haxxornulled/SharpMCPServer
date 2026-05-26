using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public readonly struct JsonRpcMessage
{
    public JsonRpcMessage(
        string? jsonRpc,
        string? method,
        JsonRpcRequestId id,
        JsonElement? parameters,
        JsonElement? result,
        JsonElement? error,
        bool hasResult,
        bool hasError)
    {
        JsonRpc = jsonRpc;
        Method = method;
        Id = id;
        Params = parameters;
        Result = result;
        Error = error;
        HasResult = hasResult;
        HasError = hasError;
    }

    public string? JsonRpc { get; }

    public string? Method { get; }

    public JsonRpcRequestId Id { get; }

    public JsonElement? Params { get; }

    public JsonElement? Result { get; }

    public JsonElement? Error { get; }

    public bool HasResult { get; }

    public bool HasError { get; }

    public bool HasId => Id.IsSpecified;

    public bool HasParams => Params.HasValue;

    public bool HasMethod => Method is { } method && !string.IsNullOrWhiteSpace(method);

    public bool IsRequest => this is { HasId: true, HasMethod: true, IsResponse: false };

    public bool IsNotification => this is { HasId: false, HasMethod: true, IsResponse: false };

    public bool IsResponse => this is { HasMethod: false } && (HasResult || HasError);
}
