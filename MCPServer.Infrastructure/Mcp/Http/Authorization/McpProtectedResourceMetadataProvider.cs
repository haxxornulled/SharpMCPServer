namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public sealed class McpProtectedResourceMetadataProvider : IMcpProtectedResourceMetadataProvider
{
    private const string ProtectedResourceMetadataPrefix = "/.well-known/oauth-protected-resource";

    private readonly StreamableHttpMcpTransportOptions _options;

    public McpProtectedResourceMetadataProvider(StreamableHttpMcpTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public bool IsProtectedResourceMetadataRequest(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);

        var path = requestUri.AbsolutePath;
        return string.Equals(path, ProtectedResourceMetadataPrefix, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(ProtectedResourceMetadataPrefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    public Uri GetResourceMetadataUri(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);

        return new Uri(requestUri.GetLeftPart(UriPartial.Authority) + ProtectedResourceMetadataPrefix);
    }

    public McpProtectedResourceMetadataDocument CreateDocument(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);

        var resourceUri = BuildResourceUri(requestUri);
        var authorizationServers = _options.Authorization.AuthorizationServers
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var scopesSupported = GetScopesSupported();

        return new McpProtectedResourceMetadataDocument
        {
            Resource = resourceUri.AbsoluteUri,
            AuthorizationServers = authorizationServers,
            ScopesSupported = scopesSupported.Length == 0 ? null : scopesSupported
        };
    }

    private Uri BuildResourceUri(Uri requestUri)
    {
        var normalizedPath = StreamableHttpMcpTransportOptions.NormalizePath(_options.Path);
        return new Uri(requestUri.GetLeftPart(UriPartial.Authority) + normalizedPath);
    }

    private string[] GetScopesSupported()
    {
        if (_options.Authorization.ScopesSupported.Count > 0)
        {
            return _options.Authorization.ScopesSupported
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        if (_options.Authorization.RequiredScopes.Count > 0)
        {
            return _options.Authorization.RequiredScopes
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        return Array.Empty<string>();
    }
}
