using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public readonly struct McpProgressToken : IEquatable<McpProgressToken>
{
    private readonly JsonElement _value;

    private McpProgressToken(JsonElement value)
    {
        _value = value;
        IsSpecified = true;
    }

    public bool IsSpecified { get; }

    public static McpProgressToken Missing => default;

    public static bool TryFromElement(JsonElement value, out McpProgressToken token)
    {
        if (!IsValidProgressToken(value))
        {
            token = Missing;
            return false;
        }

        token = new McpProgressToken(value.Clone());
        return true;
    }

    public bool TryGetStableKey(out string key)
    {
        if (!IsSpecified || !IsValidProgressToken(_value))
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
            writer.WriteNullValue();
            return;
        }

        _value.WriteTo(writer);
    }

    public bool Equals(McpProgressToken other)
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
        return obj is McpProgressToken other && Equals(other);
    }

    public override int GetHashCode()
    {
        return IsSpecified ? StringComparer.Ordinal.GetHashCode(_value.GetRawText()) : 0;
    }

    private static bool IsValidProgressToken(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => true,
            JsonValueKind.Number => value.TryGetInt64(out _),
            _ => false
        };
    }
}
