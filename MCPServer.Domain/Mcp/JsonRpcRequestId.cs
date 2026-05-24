using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public readonly struct JsonRpcRequestId : IEquatable<JsonRpcRequestId>
{
    private readonly JsonElement _value;

    private JsonRpcRequestId(JsonElement value)
    {
        _value = value;
        IsSpecified = true;
    }

    public bool IsSpecified { get; }

    public static JsonRpcRequestId Missing => default;

    public static JsonRpcRequestId FromClonedElement(JsonElement value)
    {
        if (!IsValidMcpRequestId(value))
        {
            throw new ArgumentException("MCP JSON-RPC request IDs must be strings or integers.", nameof(value));
        }

        return new JsonRpcRequestId(value);
    }

    public static bool TryFromElement(JsonElement value, out JsonRpcRequestId requestId)
    {
        if (!IsValidMcpRequestId(value))
        {
            requestId = Missing;
            return false;
        }

        requestId = new JsonRpcRequestId(value.Clone());
        return true;
    }

    public bool TryGetStableKey(out string key)
    {
        if (!IsSpecified || !IsValidMcpRequestId(_value))
        {
            key = string.Empty;
            return false;
        }

        key = _value.GetRawText();
        return true;
    }

    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (!IsSpecified)
        {
            throw new InvalidOperationException("A missing MCP request id must be omitted, not serialized as null.");
        }

        _value.WriteTo(writer);
    }

    public bool Equals(JsonRpcRequestId other)
    {
        if (IsSpecified != other.IsSpecified)
        {
            return false;
        }

        if (!IsSpecified)
        {
            return true;
        }

        return string.Equals(_value.GetRawText(), other._value.GetRawText(), StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is JsonRpcRequestId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return IsSpecified ? StringComparer.Ordinal.GetHashCode(_value.GetRawText()) : 0;
    }

    private static bool IsValidMcpRequestId(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => true,
            JsonValueKind.Number => value.TryGetInt64(out _),
            _ => false
        };
    }
}
