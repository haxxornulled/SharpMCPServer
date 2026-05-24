namespace MCPServer.Domain.Mcp;

public static class McpMetaKeyValidator
{
    public static bool IsValid(string key)
    {
        if (key.Length == 0)
        {
            return true;
        }

        var slashIndex = key.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex < 0)
        {
            return IsValidName(key.AsSpan());
        }

        if (slashIndex == 0 || key.IndexOf('/', slashIndex + 1) >= 0)
        {
            return false;
        }

        return IsValidPrefix(key.AsSpan(0, slashIndex)) && IsValidName(key.AsSpan(slashIndex + 1));
    }

    public static bool TryValidateObjectKeys(System.Text.Json.JsonElement meta, out string error)
    {
        if (meta.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            error = "MCP _meta must be a JSON object when present.";
            return false;
        }

        foreach (var property in meta.EnumerateObject())
        {
            if (!IsValid(property.Name))
            {
                error = $"MCP _meta key '{property.Name}' is invalid.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool IsValidPrefix(ReadOnlySpan<char> prefix)
    {
        if (prefix.IsEmpty)
        {
            return false;
        }

        var labelStart = 0;
        for (var i = 0; i <= prefix.Length; i++)
        {
            if (i != prefix.Length && prefix[i] != '.')
            {
                continue;
            }

            if (!IsValidPrefixLabel(prefix[labelStart..i]))
            {
                return false;
            }

            labelStart = i + 1;
        }

        return true;
    }

    private static bool IsValidPrefixLabel(ReadOnlySpan<char> label)
    {
        if (label.IsEmpty || !IsAsciiLetter(label[0]) || !IsAsciiLetterOrDigit(label[^1]))
        {
            return false;
        }

        for (var i = 1; i < label.Length - 1; i++)
        {
            var value = label[i];
            if (!IsAsciiLetterOrDigit(value) && value != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty)
        {
            return true;
        }

        if (!IsAsciiLetterOrDigit(name[0]) || !IsAsciiLetterOrDigit(name[^1]))
        {
            return false;
        }

        for (var i = 1; i < name.Length - 1; i++)
        {
            var value = name[i];
            if (!IsAsciiLetterOrDigit(value) && value is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetterOrDigit(char value)
    {
        return IsAsciiLetter(value) || value is >= '0' and <= '9';
    }

    private static bool IsAsciiLetter(char value)
    {
        return value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }
}
