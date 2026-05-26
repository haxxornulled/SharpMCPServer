using System.Text;

namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public static class McpBearerChallengeBuilder
{
    public static string Build(Uri resourceMetadataUri, IReadOnlyList<string> scopes, string? error = null, string? errorDescription = null)
    {
        ArgumentNullException.ThrowIfNull(resourceMetadataUri);
        ArgumentNullException.ThrowIfNull(scopes);

        var parts = new List<string>
        {
            $"resource_metadata=\"{Escape(resourceMetadataUri.AbsoluteUri)}\""
        };

        if (scopes.Count > 0)
        {
            parts.Add($"scope=\"{Escape(string.Join(' ', scopes))}\"");
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            parts.Add($"error=\"{Escape(error)}\"");
        }

        if (!string.IsNullOrWhiteSpace(errorDescription))
        {
            parts.Add($"error_description=\"{Escape(errorDescription)}\"");
        }

        return "Bearer " + string.Join(", ", parts);
    }

    private static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            if (character is '\\' or '"')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
