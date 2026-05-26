namespace MCPServer.Client.Authorization;

public static class McpWwwAuthenticateParser
{
    public static McpAuthorizationChallenge? TryParse(IEnumerable<string> headerValues)
    {
        foreach (var value in headerValues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var span = value.AsSpan().Trim();
            const string bearer = "Bearer";
            if (!span.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var remainder = span[bearer.Length..].TrimStart();
            foreach (var segment in remainder.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var equalsIndex = segment.IndexOf('=');
                if (equalsIndex <= 0 || equalsIndex == segment.Length - 1)
                {
                    continue;
                }

                var name = segment[..equalsIndex].Trim();
                var rawValue = segment[(equalsIndex + 1)..].Trim();
                if (rawValue.Length >= 2 && rawValue[0] == '"' && rawValue[^1] == '"')
                {
                    rawValue = rawValue[1..^1];
                }

                parameters[name] = rawValue;
            }

            parameters.TryGetValue("realm", out var realm);
            parameters.TryGetValue("scope", out var scope);
            parameters.TryGetValue("resource_metadata", out var resourceMetadata);
            Uri? resourceMetadataUri = null;
            if (!string.IsNullOrWhiteSpace(resourceMetadata) && Uri.TryCreate(resourceMetadata, UriKind.Absolute, out var parsed))
            {
                resourceMetadataUri = parsed;
            }

            return new McpAuthorizationChallenge
            {
                Scheme = bearer,
                Realm = realm,
                Scope = scope,
                ResourceMetadataUri = resourceMetadataUri
            };
        }

        return null;
    }
}
