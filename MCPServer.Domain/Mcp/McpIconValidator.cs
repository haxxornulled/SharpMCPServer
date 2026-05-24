namespace MCPServer.Domain.Mcp;

public static class McpIconValidator
{
    public static bool IsValidSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https";
    }

    public static bool IsValidTheme(string? theme)
    {
        return theme is null or "light" or "dark";
    }

    public static bool IsValidMimeType(string? mimeType)
    {
        if (mimeType is not { Length: > 0 })
        {
            return true;
        }

        return mimeType is "image/png" or "image/jpeg" or "image/jpg" or "image/svg+xml" or "image/webp";
    }
}
