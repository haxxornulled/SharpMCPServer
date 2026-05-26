using System.Net.Http.Json;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;

namespace MCPServer.Client.Authorization;

public static class McpAuthorizationServerDiscovery
{
    private const string PkceMethod = "S256";

    public static async ValueTask<Fin<McpAuthorizationServerDescriptor>> DiscoverAsync(
        HttpClient httpClient,
        Uri issuer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(issuer);

        foreach (var candidate in BuildOAuthMetadataCandidates(issuer))
        {
            try
            {
                var metadata = await TryReadOAuthMetadataAsync(httpClient, candidate, cancellationToken).ConfigureAwait(false);
                if (metadata is not null)
                {
                    return metadata;
                }
            }
            catch (InvalidOperationException ex)
            {
                return Fin.Fail<McpAuthorizationServerDescriptor>(Error.New(ex.Message));
            }
        }

        foreach (var candidate in BuildOidcCandidates(issuer))
        {
            try
            {
                var metadata = await TryReadOidcMetadataAsync(httpClient, candidate, cancellationToken).ConfigureAwait(false);
                if (metadata is not null)
                {
                    return metadata;
                }
            }
            catch (InvalidOperationException ex)
            {
                return Fin.Fail<McpAuthorizationServerDescriptor>(Error.New(ex.Message));
            }
        }

        return Fin.Fail<McpAuthorizationServerDescriptor>(Error.New($"Unable to resolve OAuth or OpenID Connect metadata for issuer '{issuer}'."));
    }

    public static async ValueTask<Fin<McpAuthorizationServerMetadata>> DiscoverOAuthMetadataAsync(
        HttpClient httpClient,
        Uri issuer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(issuer);

        foreach (var candidate in BuildOAuthMetadataCandidates(issuer))
        {
            try
            {
                var metadata = await httpClient.GetFromJsonAsync(candidate, McpAuthorizationJsonSerializerContext.Default.McpAuthorizationServerMetadata, cancellationToken).ConfigureAwait(false);
                if (metadata is not null)
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

        return Fin.Fail<McpAuthorizationServerMetadata>(Error.New($"Unable to resolve OAuth authorization server metadata for issuer '{issuer}'."));
    }

    public static async ValueTask<Fin<McpOidcDiscoveryDocument>> DiscoverOidcMetadataAsync(
        HttpClient httpClient,
        Uri issuer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(issuer);

        foreach (var candidate in BuildOidcCandidates(issuer))
        {
            try
            {
                var metadata = await httpClient.GetFromJsonAsync(candidate, McpAuthorizationJsonSerializerContext.Default.McpOidcDiscoveryDocument, cancellationToken).ConfigureAwait(false);
                if (metadata is not null)
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

        return Fin.Fail<McpOidcDiscoveryDocument>(Error.New($"Unable to resolve OpenID Connect discovery metadata for issuer '{issuer}'."));
    }

    private static async ValueTask<McpAuthorizationServerDescriptor?> TryReadOAuthMetadataAsync(HttpClient httpClient, Uri candidate, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await httpClient.GetFromJsonAsync(candidate, McpAuthorizationJsonSerializerContext.Default.McpAuthorizationServerMetadata, cancellationToken).ConfigureAwait(false);
            if (metadata is null)
            {
                return null;
            }

            return ConvertOAuthMetadata(candidate, metadata);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static async ValueTask<McpAuthorizationServerDescriptor?> TryReadOidcMetadataAsync(HttpClient httpClient, Uri candidate, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await httpClient.GetFromJsonAsync(candidate, McpAuthorizationJsonSerializerContext.Default.McpOidcDiscoveryDocument, cancellationToken).ConfigureAwait(false);
            if (metadata is null)
            {
                return null;
            }

            return ConvertOidcMetadata(candidate, metadata);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static IEnumerable<Uri> BuildOAuthMetadataCandidates(Uri issuer)
    {
        var baseUri = issuer.GetLeftPart(UriPartial.Authority);
        var path = issuer.AbsolutePath.Trim('/');
        if (path.Length == 0)
        {
            yield return new Uri(baseUri + "/.well-known/oauth-authorization-server");
            yield break;
        }

        yield return new Uri(baseUri + "/.well-known/oauth-authorization-server/" + path);
    }

    private static IEnumerable<Uri> BuildOidcCandidates(Uri issuer)
    {
        var baseUri = issuer.GetLeftPart(UriPartial.Authority);
        var path = issuer.AbsolutePath.Trim('/');
        if (path.Length == 0)
        {
            yield return new Uri(baseUri + "/.well-known/openid-configuration");
            yield break;
        }

        yield return new Uri(baseUri + "/.well-known/openid-configuration/" + path);
        yield return new Uri(baseUri + "/" + path + "/.well-known/openid-configuration");
    }

    private static McpAuthorizationServerDescriptor ConvertOAuthMetadata(Uri metadataUri, McpAuthorizationServerMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadataUri);
        ArgumentNullException.ThrowIfNull(metadata);

        var issuer = ParseAbsoluteUri(metadata.Issuer) ?? metadataUri;
        var authorizationEndpoint = ParseAbsoluteUri(metadata.AuthorizationEndpoint)
            ?? throw new InvalidOperationException($"The OAuth authorization server metadata at '{metadataUri}' did not include a valid authorization_endpoint.");
        var tokenEndpoint = ParseAbsoluteUri(metadata.TokenEndpoint)
            ?? throw new InvalidOperationException($"The OAuth authorization server metadata at '{metadataUri}' did not include a valid token_endpoint.");
        var registrationEndpoint = ParseAbsoluteUri(metadata.RegistrationEndpoint);
        var codeChallengeMethods = NormalizeCodeChallengeMethods(metadata.CodeChallengeMethodsSupported, metadataUri, discoveryKind: "OAuth 2.0 authorization server metadata");

        EnsurePkceSupported(codeChallengeMethods, metadataUri, discoveryKind: "OAuth 2.0 authorization server metadata");

        return new McpAuthorizationServerDescriptor
        {
            MetadataUri = metadataUri,
            Issuer = issuer,
            AuthorizationEndpoint = authorizationEndpoint,
            TokenEndpoint = tokenEndpoint,
            RegistrationEndpoint = registrationEndpoint,
            ClientIdMetadataDocumentSupported = metadata.ClientIdMetadataDocumentSupported ?? false,
            CodeChallengeMethodsSupported = codeChallengeMethods,
            DiscoverySource = McpAuthorizationServerDiscoverySource.OAuthAuthorizationServerMetadata
        };
    }

    private static McpAuthorizationServerDescriptor ConvertOidcMetadata(Uri metadataUri, McpOidcDiscoveryDocument metadata)
    {
        ArgumentNullException.ThrowIfNull(metadataUri);
        ArgumentNullException.ThrowIfNull(metadata);

        var issuer = ParseAbsoluteUri(metadata.Issuer)
            ?? throw new InvalidOperationException($"The OpenID Connect discovery document at '{metadataUri}' did not include a valid issuer.");
        var authorizationEndpoint = ParseAbsoluteUri(metadata.AuthorizationEndpoint)
            ?? throw new InvalidOperationException($"The OpenID Connect discovery document at '{metadataUri}' did not include a valid authorization_endpoint.");
        var tokenEndpoint = ParseAbsoluteUri(metadata.TokenEndpoint)
            ?? throw new InvalidOperationException($"The OpenID Connect discovery document at '{metadataUri}' did not include a valid token_endpoint.");
        var registrationEndpoint = ParseAbsoluteUri(metadata.RegistrationEndpoint);
        var codeChallengeMethods = NormalizeCodeChallengeMethods(metadata.CodeChallengeMethodsSupported, metadataUri, discoveryKind: "OpenID Connect discovery document");

        EnsurePkceSupported(codeChallengeMethods, metadataUri, discoveryKind: "OpenID Connect discovery document");

        return new McpAuthorizationServerDescriptor
        {
            MetadataUri = metadataUri,
            Issuer = issuer,
            AuthorizationEndpoint = authorizationEndpoint,
            TokenEndpoint = tokenEndpoint,
            RegistrationEndpoint = registrationEndpoint,
            ClientIdMetadataDocumentSupported = false,
            CodeChallengeMethodsSupported = codeChallengeMethods,
            DiscoverySource = McpAuthorizationServerDiscoverySource.OpenIdConnectDiscovery
        };
    }

    private static Uri? ParseAbsoluteUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed) ? parsed : null;
    }

    private static string[] NormalizeCodeChallengeMethods(string[]? methods, Uri metadataUri, string discoveryKind)
    {
        if (methods is null || methods.Length == 0)
        {
            throw new InvalidOperationException($"The {discoveryKind} at '{metadataUri}' did not advertise code_challenge_methods_supported.");
        }

        return methods
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void EnsurePkceSupported(IReadOnlyCollection<string> codeChallengeMethods, Uri metadataUri, string discoveryKind)
    {
        foreach (var method in codeChallengeMethods)
        {
            if (string.Equals(method, PkceMethod, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new InvalidOperationException($"The {discoveryKind} at '{metadataUri}' did not advertise support for the required S256 PKCE code challenge method.");
    }
}
