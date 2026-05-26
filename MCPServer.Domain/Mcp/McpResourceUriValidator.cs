namespace MCPServer.Domain.Mcp;

public static class McpResourceUriValidator
{
    public static bool IsValid(string? uri)
    {
        if (uri is not { Length: > 0 } value || value.Length > 4_096)
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        return parsed.Scheme is { Length: > 0 } scheme && !IsUnsafeScheme(scheme);
    }

    private static bool IsUnsafeScheme(string scheme)
    {
        return scheme.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
               scheme.Equals("data", StringComparison.OrdinalIgnoreCase);
    }
}
