using System.Globalization;
using System.Text;

namespace MCPServer.Domain.Mcp;

public static class McpOpaqueCursor
{
    private const string ToolsListPrefix = "mcp-tools-list-v1.";
    private const string ResourcesListPrefix = "mcp-resources-list-v1.";
    private const string PromptsListPrefix = "mcp-prompts-list-v1.";

    public static string CreateToolsListCursor(int start, int itemCount)
    {
        return CreateCursor(start, itemCount, ToolsListPrefix);
    }

    private static string CreateCursor(int start, int itemCount, string prefix)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, "Cursor start must not be negative.");
        }

        if (itemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemCount), itemCount, "Cursor item count must not be negative.");
        }

        if (start > itemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, "Cursor start must not exceed the item count.");
        }

        var payload = start.ToString(CultureInfo.InvariantCulture) + ":" + itemCount.ToString(CultureInfo.InvariantCulture);
        return prefix + EncodeBase64Url(payload);
    }

    public static bool TryReadToolsListCursor(string? cursor, int itemCount, out int start)
    {
        return TryReadCursor(cursor, itemCount, ToolsListPrefix, out start);
    }

    public static string CreateResourcesListCursor(int start, int itemCount)
    {
        return CreateCursor(start, itemCount, ResourcesListPrefix);
    }

    public static bool TryReadResourcesListCursor(string? cursor, int itemCount, out int start)
    {
        return TryReadCursor(cursor, itemCount, ResourcesListPrefix, out start);
    }

    public static string CreatePromptsListCursor(int start, int itemCount)
    {
        return CreateCursor(start, itemCount, PromptsListPrefix);
    }

    public static bool TryReadPromptsListCursor(string? cursor, int itemCount, out int start)
    {
        return TryReadCursor(cursor, itemCount, PromptsListPrefix, out start);
    }

    private static bool TryReadCursor(string? cursor, int itemCount, string prefix, out int start)
    {
        start = 0;

        if (cursor is not { Length: > 0 } cursorValue || itemCount < 0)
        {
            return false;
        }

        if (!cursorValue.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var encodedPayload = cursorValue.AsSpan(prefix.Length);
        if (encodedPayload.IsEmpty || !TryDecodeBase64Url(encodedPayload, out var payload))
        {
            return false;
        }

        var separator = payload.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == payload.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(payload.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedStart) ||
            !int.TryParse(payload.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedItemCount))
        {
            return false;
        }

        if (parsedItemCount != itemCount || parsedStart < 0 || parsedStart > itemCount)
        {
            return false;
        }

        start = parsedStart;
        return true;
    }

    private static string EncodeBase64Url(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryDecodeBase64Url(ReadOnlySpan<char> encoded, out string value)
    {
        value = string.Empty;

        if (encoded.IsEmpty)
        {
            return false;
        }

        var base64 = encoded.ToString().Replace('-', '+').Replace('_', '/');
        var padding = base64.Length % 4;
        base64 = padding switch
        {
            0 => base64,
            2 => base64 + "==",
            3 => base64 + "=",
            _ => string.Empty
        };

        if (base64.Length == 0)
        {
            return false;
        }

        try
        {
            var decoded = Convert.FromBase64String(base64);
            value = Encoding.UTF8.GetString(decoded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
