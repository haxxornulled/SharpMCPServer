using System.Net.Http.Json;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;

namespace MCPServer.Client.Authorization;

public static class McpProtectedResourceMetadataResolver
{
    public static async ValueTask<Fin<McpProtectedResourceMetadata>> ResolveAsync(
        HttpClient httpClient,
        Uri endpoint,
        McpAuthorizationChallenge? challenge,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpoint);

        var candidates = new List<Uri>();
        if (challenge is { ResourceMetadataUri: { } resourceMetadataUri } && IsSameOrigin(endpoint, resourceMetadataUri))
        {
            candidates.Add(resourceMetadataUri);
        }

        var endpointPath = endpoint.AbsolutePath.Trim('/');
        if (endpointPath.Length != 0)
        {
            candidates.Add(new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/.well-known/oauth-protected-resource/" + endpointPath));
        }

        candidates.Add(new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/.well-known/oauth-protected-resource"));

        foreach (var candidate in candidates.Distinct())
        {
            try
            {
                var metadata = await httpClient.GetFromJsonAsync(candidate, McpAuthorizationJsonSerializerContext.Default.McpProtectedResourceMetadata, cancellationToken).ConfigureAwait(false);
                if (metadata is { })
                {
                    return Fin.Succ(metadata);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
            }
            catch (JsonException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return Fin.Fail<McpProtectedResourceMetadata>(Error.New("Unable to resolve MCP protected resource metadata from the configured endpoint."));
    }

    private static bool IsSameOrigin(Uri left, Uri right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
               left.Port == right.Port;
    }
}
