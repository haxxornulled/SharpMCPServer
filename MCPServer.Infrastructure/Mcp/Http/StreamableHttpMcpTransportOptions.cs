namespace MCPServer.Infrastructure.Mcp.Http;

public sealed class StreamableHttpMcpTransportOptions
{
    public bool Enabled { get; init; }

    public int Port { get; init; }

    public string Path { get; init; } = "/mcp/";

    public bool BindLoopbackOnly { get; init; } = true;

    public int SseRetryMilliseconds { get; init; } = 3_000;

    public int SseHeartbeatMilliseconds { get; init; } = 15_000;

    public int MaxSessionHistoryMessages { get; init; } = 256;

    public StreamableHttpMcpAuthorizationOptions Authorization { get; init; } = new();

    public IReadOnlyList<string> GetListenerPrefixes()
    {
        if (!Enabled)
        {
            return Array.Empty<string>();
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Port);

        if (Authorization.Enabled)
        {
            if (BindLoopbackOnly)
            {
                return
                [
                    $"http://127.0.0.1:{Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/",
                    $"http://localhost:{Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/",
                    $"http://[::1]:{Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/"
                ];
            }

            return [$"http://*:{Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/"];
        }

        var normalizedPath = NormalizePath(Path);
        if (BindLoopbackOnly)
        {
            return
            [
                $"http://127.0.0.1:{Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}{normalizedPath}",
                $"http://localhost:{Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}{normalizedPath}",
                $"http://[::1]:{Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}{normalizedPath}"
            ];
        }

        return [$"http://*:{Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}{normalizedPath}"];
    }

    public Uri GetLocalOrigin()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Port);
        return new Uri($"http://127.0.0.1:{Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }

    public static string NormalizePath(string value)
    {
        var path = string.IsNullOrWhiteSpace(value) ? "/mcp/" : value.Trim();
        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            path = "/" + path;
        }

        if (!path.EndsWith("/", StringComparison.Ordinal))
        {
            path += "/";
        }

        return path;
    }
}

public sealed class StreamableHttpMcpAuthorizationOptions
{
    public bool Enabled { get; init; }

    public IReadOnlyList<string> AuthorizationServers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RequiredScopes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ScopesSupported { get; init; } = Array.Empty<string>();

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (AuthorizationServers.Count == 0)
        {
            throw new InvalidOperationException("Streamable HTTP authorization is enabled but no authorization servers were configured.");
        }

        foreach (var authorizationServer in AuthorizationServers)
        {
            if (!Uri.TryCreate(authorizationServer, UriKind.Absolute, out var parsed) ||
                (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Invalid authorization server URI '{authorizationServer}'.");
            }
        }

        foreach (var scope in RequiredScopes.Concat(ScopesSupported))
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new InvalidOperationException("Streamable HTTP authorization scope values must not be blank.");
            }
        }
    }
}
