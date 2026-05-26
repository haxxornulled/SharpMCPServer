using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Client.Authorization;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPServer.Client.Infrastructure.Authorization;

public sealed class McpOAuthAuthorizationCodeProvider : IMcpAuthorizationProvider, IAsyncDisposable, IDisposable
{
    private readonly McpOAuthAuthorizationProviderOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMcpBrowserLauncher _browserLauncher;
    private readonly ILogger<McpOAuthAuthorizationCodeProvider> _logger;
    private readonly SemaphoreSlim _authorizationLock = new SemaphoreSlim(1, 1);
    private McpOAuthAuthorizationState? _cachedAuthorization;
    private bool _disposed;

    public McpOAuthAuthorizationCodeProvider(
        McpOAuthAuthorizationProviderOptions options,
        IHttpClientFactory httpClientFactory,
        IMcpBrowserLauncher browserLauncher,
        ILogger<McpOAuthAuthorizationCodeProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(browserLauncher);

        _options = options;
        _httpClientFactory = httpClientFactory;
        _browserLauncher = browserLauncher;
        _logger = logger ?? NullLogger<McpOAuthAuthorizationCodeProvider>.Instance;
    }

    public async ValueTask<Fin<string>> GetAccessTokenAsync(McpAuthorizationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var authorizationServer = SelectAuthorizationServer(context.AuthorizationServers);
        if (authorizationServer is null)
        {
            return Fin.Fail<string>(Error.New("The MCP authorization context did not include a usable authorization server."));
        }

        var resourceUri = NormalizeResourceUri(context.Endpoint);
        var requiredScopes = NormalizeScopes(context.RequiredScopes);

        if (context.Challenge is null && TryGetCachedAccessToken(authorizationServer, resourceUri, requiredScopes, out var cachedAccessToken))
        {
            _logger.LogDebug(
                "Reusing cached MCP access token for resource {ResourceUri} against authorization server {AuthorizationServer}.",
                resourceUri,
                authorizationServer.Issuer);
            return Fin.Succ(cachedAccessToken);
        }

        await _authorizationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (context.Challenge is null && TryGetCachedAccessToken(authorizationServer, resourceUri, requiredScopes, out cachedAccessToken))
            {
                return Fin.Succ(cachedAccessToken);
            }

            using var httpClient = CreateHttpClient();
            if (_cachedAuthorization is { } cachedAuthorization &&
                cachedAuthorization.Matches(authorizationServer, resourceUri) &&
                cachedAuthorization.RefreshToken is not null &&
                !string.IsNullOrWhiteSpace(cachedAuthorization.ClientId))
            {
                var refreshResult = await TryRefreshAccessTokenAsync(
                    httpClient,
                    authorizationServer,
                    cachedAuthorization,
                    resourceUri,
                    requiredScopes,
                    cancellationToken).ConfigureAwait(false);

                if (refreshResult.IsSucc)
                {
                    return refreshResult;
                }

                _logger.LogDebug(
                    "Refresh-token grant did not yield a usable MCP token for authorization server {AuthorizationServer}; falling back to interactive authorization.",
                    authorizationServer.Issuer);
            }

            var pkceState = McpPkceUtilities.CreateState();
            var codeVerifier = McpPkceUtilities.CreateCodeVerifier();
            var codeChallenge = McpPkceUtilities.CreateCodeChallenge(codeVerifier);

            var redirectLeaseResult = await StartLoopbackLeaseAsync(pkceState, cancellationToken).ConfigureAwait(false);
            if (redirectLeaseResult.IsFail)
            {
                return redirectLeaseResult.Match(
                    Succ: static _ => throw new InvalidOperationException(),
                    Fail: static error => Fin.Fail<string>(error));
            }

            await using var redirectLease = redirectLeaseResult.Match(
                Succ: static lease => lease,
                Fail: static _ => throw new InvalidOperationException());

            var clientIdentifierResult = await ResolveClientIdentifierAsync(
                httpClient,
                authorizationServer,
                redirectLease.RedirectUri,
                cancellationToken).ConfigureAwait(false);

            if (clientIdentifierResult.IsFail)
            {
                return clientIdentifierResult.Match(
                    Succ: static _ => throw new InvalidOperationException(),
                    Fail: static error => Fin.Fail<string>(error));
            }

            var clientIdentifier = clientIdentifierResult.Match(
                Succ: static value => value,
                Fail: static _ => throw new InvalidOperationException());

            return await RunAuthorizationCodeFlowAsync(
                httpClient,
                authorizationServer,
                clientIdentifier,
                redirectLease,
                pkceState,
                codeVerifier,
                codeChallenge,
                resourceUri,
                requiredScopes,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _authorizationLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _authorizationLock.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask<Fin<string>> RunAuthorizationCodeFlowAsync(
        HttpClient httpClient,
        McpAuthorizationServerDescriptor authorizationServer,
        string clientIdentifier,
        McpLoopbackAuthorizationCodeLease redirectLease,
        string state,
        string codeVerifier,
        string codeChallenge,
        string resourceUri,
        IReadOnlyList<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        var authorizationRequestUri = BuildAuthorizationRequestUri(
            authorizationServer.AuthorizationEndpoint,
            clientIdentifier,
            redirectLease.RedirectUri,
            state,
            codeChallenge,
            resourceUri,
            requiredScopes);

        _logger.LogInformation(
            "Starting MCP OAuth authorization code flow for resource {ResourceUri} against {AuthorizationServer}.",
            resourceUri,
            authorizationServer.Issuer);

        if (!_browserLauncher.TryLaunch(authorizationRequestUri, out var browserError))
        {
            return Fin.Fail<string>(Error.New(string.IsNullOrWhiteSpace(browserError)
                ? "Failed to launch the system browser."
                : browserError));
        }

        var callbackResult = await redirectLease.WaitForResultAsync(_options.AuthorizationTimeout, cancellationToken).ConfigureAwait(false);
        if (callbackResult.IsFail)
        {
            return callbackResult.Match(
                Succ: static _ => throw new InvalidOperationException(),
                Fail: static error => Fin.Fail<string>(error));
        }

        var authorizationCode = callbackResult.Match(
            Succ: static value => value,
            Fail: static _ => throw new InvalidOperationException());

        var tokenResult = await ExchangeAuthorizationCodeAsync(
            httpClient,
            authorizationServer,
            clientIdentifier,
            redirectLease.RedirectUri,
            authorizationCode.Code,
            codeVerifier,
            resourceUri,
            requiredScopes,
            cancellationToken).ConfigureAwait(false);

        return tokenResult;
    }

    private async ValueTask<Fin<string>> TryRefreshAccessTokenAsync(
        HttpClient httpClient,
        McpAuthorizationServerDescriptor authorizationServer,
        McpOAuthAuthorizationState cachedAuthorization,
        string resourceUri,
        IReadOnlyList<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cachedAuthorization.RefreshToken))
        {
            return Fin.Fail<string>(Error.New("No refresh token is available."));
        }

        var refreshRequestResult = await CreateTokenRequestAsync(
            httpClient,
            authorizationServer.TokenEndpoint,
            cachedAuthorization.ClientId,
            resourceUri,
            cachedAuthorization.RefreshToken,
            grantType: "refresh_token",
            requiredScopes,
            codeVerifier: null,
            redirectUri: null,
            cancellationToken).ConfigureAwait(false);

        if (refreshRequestResult.IsFail)
        {
            return refreshRequestResult.Match(
                Succ: static _ => throw new InvalidOperationException(),
                Fail: static error => Fin.Fail<string>(error));
        }

        var tokenResponse = refreshRequestResult.Match(
            Succ: static value => value,
            Fail: static _ => throw new InvalidOperationException());

        return StoreTokenResponse(authorizationServer, cachedAuthorization.ClientId, resourceUri, requiredScopes, tokenResponse);
    }

    private async ValueTask<Fin<string>> ExchangeAuthorizationCodeAsync(
        HttpClient httpClient,
        McpAuthorizationServerDescriptor authorizationServer,
        string clientIdentifier,
        Uri redirectUri,
        string authorizationCode,
        string codeVerifier,
        string resourceUri,
        IReadOnlyList<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        var tokenResponseResult = await CreateTokenRequestAsync(
            httpClient,
            authorizationServer.TokenEndpoint,
            clientIdentifier,
            resourceUri,
            authorizationCode,
            grantType: "authorization_code",
            requiredScopes,
            codeVerifier,
            redirectUri,
            cancellationToken).ConfigureAwait(false);

        if (tokenResponseResult.IsFail)
        {
            return tokenResponseResult.Match(
                Succ: static _ => throw new InvalidOperationException(),
                Fail: static error => Fin.Fail<string>(error));
        }

        var tokenResponse = tokenResponseResult.Match(
            Succ: static value => value,
            Fail: static _ => throw new InvalidOperationException());

        return StoreTokenResponse(authorizationServer, clientIdentifier, resourceUri, requiredScopes, tokenResponse);
    }

    private async ValueTask<Fin<McpOAuthTokenResponse>> CreateTokenRequestAsync(
        HttpClient httpClient,
        Uri tokenEndpoint,
        string clientIdentifier,
        string resourceUri,
        string credential,
        string grantType,
        IReadOnlyList<string> requiredScopes,
        string? codeVerifier,
        Uri? redirectUri,
        CancellationToken cancellationToken)
    {
        using var requestContentBuilder = new McpUrlEncodedBufferBuilder(256);
        requestContentBuilder.AppendField("grant_type", grantType);
        requestContentBuilder.AppendField(grantType == "refresh_token" ? "refresh_token" : "code", credential);
        requestContentBuilder.AppendField("client_id", clientIdentifier);
        requestContentBuilder.AppendField("resource", resourceUri);

        if (redirectUri is not null)
        {
            requestContentBuilder.AppendField("redirect_uri", redirectUri.AbsoluteUri);
        }

        if (!string.IsNullOrWhiteSpace(codeVerifier))
        {
            requestContentBuilder.AppendField("code_verifier", codeVerifier);
        }

        if (grantType != "refresh_token" && requiredScopes.Count > 0)
        {
            requestContentBuilder.AppendField("scope", string.Join(' ', requiredScopes));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = requestContentBuilder.ToContent()
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var requestTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.TokenRequestTimeout > TimeSpan.Zero)
        {
            requestTimeoutCts.CancelAfter(_options.TokenRequestTimeout);
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeoutCts.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await ReadResponseTextAsync(response, requestTimeoutCts.Token).ConfigureAwait(false);
            var message = string.IsNullOrWhiteSpace(payload)
                ? $"The OAuth token endpoint returned HTTP {(int)response.StatusCode}."
                : $"The OAuth token endpoint returned HTTP {(int)response.StatusCode}: {payload}";
            return Fin.Fail<McpOAuthTokenResponse>(Error.New(message));
        }

        try
        {
            var tokenResponse = await response.Content.ReadFromJsonAsync(McpAuthorizationJsonSerializerContext.Default.McpOAuthTokenResponse, requestTimeoutCts.Token).ConfigureAwait(false);
            if (tokenResponse is null)
            {
                return Fin.Fail<McpOAuthTokenResponse>(Error.New("The OAuth token endpoint response did not contain a token payload."));
            }

            return Fin.Succ(tokenResponse);
        }
        catch (OperationCanceledException) when (requestTimeoutCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            return Fin.Fail<McpOAuthTokenResponse>(Error.New($"The OAuth token endpoint returned invalid JSON: {ex.Message}"));
        }
        catch (NotSupportedException ex)
        {
            return Fin.Fail<McpOAuthTokenResponse>(Error.New($"The OAuth token endpoint response could not be read: {ex.Message}"));
        }
    }

    private static Uri BuildAuthorizationRequestUri(
        Uri authorizationEndpoint,
        string clientIdentifier,
        Uri redirectUri,
        string state,
        string codeChallenge,
        string resourceUri,
        IReadOnlyList<string> requiredScopes)
    {
        using var queryBuilder = new McpUrlEncodedBufferBuilder(512);
        queryBuilder.AppendField("response_type", "code");
        queryBuilder.AppendField("client_id", clientIdentifier);
        queryBuilder.AppendField("redirect_uri", redirectUri.AbsoluteUri);
        queryBuilder.AppendField("state", state);
        queryBuilder.AppendField("code_challenge", codeChallenge);
        queryBuilder.AppendField("code_challenge_method", "S256");
        queryBuilder.AppendField("resource", resourceUri);

        if (requiredScopes.Count > 0)
        {
            queryBuilder.AppendField("scope", string.Join(' ', requiredScopes));
        }

        var builder = new UriBuilder(authorizationEndpoint)
        {
            Query = queryBuilder.ToAsciiString()
        };

        return builder.Uri;
    }

    private async ValueTask<Fin<string>> ResolveClientIdentifierAsync(
        HttpClient httpClient,
        McpAuthorizationServerDescriptor authorizationServer,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.ClientId))
        {
            return Fin.Succ(_options.ClientId.Trim());
        }

        if (_options.ClientIdMetadataDocumentUri is not null)
        {
            var clientIdDocumentResult = ValidateClientIdMetadataDocumentUri(_options.ClientIdMetadataDocumentUri);
            if (clientIdDocumentResult.IsFail)
            {
                return clientIdDocumentResult;
            }

            if (!authorizationServer.ClientIdMetadataDocumentSupported)
            {
                return Fin.Fail<string>(Error.New("The authorization server does not advertise client ID metadata document support."));
            }

            return Fin.Succ(_options.ClientIdMetadataDocumentUri.AbsoluteUri);
        }

        if (authorizationServer.SupportsDynamicClientRegistration && _options.UseDynamicClientRegistration)
        {
            return await RegisterClientAsync(httpClient, authorizationServer, redirectUri, cancellationToken).ConfigureAwait(false);
        }

        return Fin.Fail<string>(Error.New("No client registration strategy is available for the authorization server."));
    }

    private async ValueTask<Fin<string>> RegisterClientAsync(
        HttpClient httpClient,
        McpAuthorizationServerDescriptor authorizationServer,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        if (authorizationServer.RegistrationEndpoint is null)
        {
            return Fin.Fail<string>(Error.New("The authorization server does not expose a registration endpoint."));
        }

        var registrationRequest = new McpOAuthClientRegistrationRequest
        {
            ClientName = _options.ClientName,
            ClientUri = _options.ClientUri?.AbsoluteUri,
            RedirectUris = new[] { redirectUri.AbsoluteUri }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, authorizationServer.RegistrationEndpoint)
        {
            Content = JsonContent.Create(registrationRequest, McpAuthorizationJsonSerializerContext.Default.McpOAuthClientRegistrationRequest)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var requestTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.TokenRequestTimeout > TimeSpan.Zero)
        {
            requestTimeoutCts.CancelAfter(_options.TokenRequestTimeout);
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeoutCts.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await ReadResponseTextAsync(response, requestTimeoutCts.Token).ConfigureAwait(false);
            var message = string.IsNullOrWhiteSpace(payload)
                ? $"The OAuth client registration endpoint returned HTTP {(int)response.StatusCode}."
                : $"The OAuth client registration endpoint returned HTTP {(int)response.StatusCode}: {payload}";
            return Fin.Fail<string>(Error.New(message));
        }

        try
        {
            var registrationResponse = await response.Content.ReadFromJsonAsync(McpAuthorizationJsonSerializerContext.Default.McpOAuthClientRegistrationResponse, requestTimeoutCts.Token).ConfigureAwait(false);
            if (registrationResponse is null || string.IsNullOrWhiteSpace(registrationResponse.ClientId))
            {
                return Fin.Fail<string>(Error.New("The OAuth client registration endpoint did not return a client_id."));
            }

            if (!string.IsNullOrWhiteSpace(registrationResponse.ClientSecret))
            {
                _logger.LogWarning("The OAuth client registration endpoint returned a client secret, but the MCP client is configured for a public client and will ignore it.");
            }

            return Fin.Succ(registrationResponse.ClientId.Trim());
        }
        catch (OperationCanceledException) when (requestTimeoutCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            return Fin.Fail<string>(Error.New($"The OAuth client registration endpoint returned invalid JSON: {ex.Message}"));
        }
        catch (NotSupportedException ex)
        {
            return Fin.Fail<string>(Error.New($"The OAuth client registration endpoint response could not be read: {ex.Message}"));
        }
    }

    private async ValueTask<Fin<McpLoopbackAuthorizationCodeLease>> StartLoopbackLeaseAsync(string expectedState, CancellationToken cancellationToken)
    {
        var leaseResult = await McpLoopbackAuthorizationCodeLease.StartAsync(expectedState, _options.RedirectUri, _options.CallbackPath, cancellationToken).ConfigureAwait(false);
        if (leaseResult.IsFail)
        {
            return leaseResult.Match(
                Succ: static _ => throw new InvalidOperationException(),
                Fail: static error => Fin.Fail<McpLoopbackAuthorizationCodeLease>(error));
        }

        return leaseResult;
    }

    private Fin<string> StoreTokenResponse(
        McpAuthorizationServerDescriptor authorizationServer,
        string clientIdentifier,
        string resourceUri,
        IReadOnlyList<string> requestedScopes,
        McpOAuthTokenResponse tokenResponse)
    {
        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            return Fin.Fail<string>(Error.New("The OAuth token endpoint did not return an access token."));
        }

        if (!string.IsNullOrWhiteSpace(tokenResponse.TokenType) &&
            !string.Equals(tokenResponse.TokenType.Trim(), "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return Fin.Fail<string>(Error.New("The OAuth token endpoint did not return a Bearer access token."));
        }

        var grantedScopes = NormalizeScopes(tokenResponse.Scope is { Length: > 0 } tokenScope ? SplitScopes(tokenScope) : requestedScopes);
        if (requestedScopes.Count > 0 && !ScopesAreSatisfied(requestedScopes, grantedScopes))
        {
            return Fin.Fail<string>(Error.New("The OAuth token endpoint did not return the required scopes."));
        }

        DateTimeOffset? expiresAtUtc = null;
        if (tokenResponse.ExpiresIn is > 0)
        {
            expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn.Value);
        }

        _cachedAuthorization = new McpOAuthAuthorizationState(
            authorizationServer.Issuer.AbsoluteUri,
            resourceUri,
            clientIdentifier,
            tokenResponse.AccessToken.Trim(),
            tokenResponse.RefreshToken?.Trim(),
            grantedScopes,
            expiresAtUtc);

        _logger.LogInformation(
            "Acquired OAuth access token for resource {ResourceUri} against authorization server {AuthorizationServer}.",
            resourceUri,
            authorizationServer.Issuer);

        return Fin.Succ(_cachedAuthorization.AccessToken);
    }

    private bool TryGetCachedAccessToken(
        McpAuthorizationServerDescriptor authorizationServer,
        string resourceUri,
        IReadOnlyList<string> requiredScopes,
        out string accessToken)
    {
        accessToken = string.Empty;

        if (_cachedAuthorization is not { } cachedAuthorization)
        {
            return false;
        }

        if (!cachedAuthorization.Matches(authorizationServer, resourceUri))
        {
            return false;
        }

        if (cachedAuthorization.IsExpired(_options.RefreshSkew))
        {
            return false;
        }

        if (!ScopesAreSatisfied(requiredScopes, cachedAuthorization.GrantedScopes))
        {
            return false;
        }

        accessToken = cachedAuthorization.AccessToken;
        return true;
    }

    private HttpClient CreateHttpClient()
    {
        var httpClientName = string.IsNullOrWhiteSpace(_options.HttpClientName)
            ? nameof(McpOAuthAuthorizationCodeProvider)
            : _options.HttpClientName;

        var httpClient = _httpClientFactory.CreateClient(httpClientName);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        return httpClient;
    }

    private McpAuthorizationServerDescriptor? SelectAuthorizationServer(IReadOnlyList<McpAuthorizationServerDescriptor> authorizationServers)
    {
        if (authorizationServers.Count == 0)
        {
            return null;
        }

        if (_cachedAuthorization is { } cachedAuthorization)
        {
            for (var index = 0; index < authorizationServers.Count; index++)
            {
                var candidate = authorizationServers[index];
                if (string.Equals(cachedAuthorization.AuthorizationServerIssuer, candidate.Issuer.AbsoluteUri, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        return authorizationServers[0];
    }

    private static string NormalizeResourceUri(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var resourceUriString = endpoint.GetLeftPart(UriPartial.Path);
        if (endpoint.AbsolutePath == "/" && resourceUriString.EndsWith('/'))
        {
            resourceUriString = resourceUriString[..^1];
        }

        return resourceUriString;
    }

    private static string[] SplitScopes(string scopes)
    {
        var parts = scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return Array.Empty<string>();
        }

        var cleanedScopes = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                cleanedScopes.Add(part);
            }
        }

        return cleanedScopes.Count == 0 ? Array.Empty<string>() : cleanedScopes.ToArray();
    }

    private static string[] NormalizeScopes(IReadOnlyList<string> scopes)
    {
        if (scopes.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalizedScopes = new List<string>(scopes.Count);
        foreach (var scope in scopes)
        {
            if (!string.IsNullOrWhiteSpace(scope))
            {
                normalizedScopes.Add(scope.Trim());
            }
        }

        return normalizedScopes.Count == 0 ? Array.Empty<string>() : normalizedScopes.ToArray();
    }

    private static bool ScopesAreSatisfied(IReadOnlyList<string> requestedScopes, IReadOnlyList<string> grantedScopes)
    {
        if (requestedScopes.Count == 0)
        {
            return true;
        }

        foreach (var requestedScope in requestedScopes)
        {
            var isGranted = false;
            foreach (var grantedScope in grantedScopes)
            {
                if (string.Equals(requestedScope, grantedScope, StringComparison.Ordinal))
                {
                    isGranted = true;
                    break;
                }
            }

            if (!isGranted)
            {
                return false;
            }
        }

        return true;
    }

    private static Fin<string> ValidateClientIdMetadataDocumentUri(Uri clientIdMetadataDocumentUri)
    {
        if (!clientIdMetadataDocumentUri.IsAbsoluteUri ||
            !string.Equals(clientIdMetadataDocumentUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(clientIdMetadataDocumentUri.Query) ||
            !string.IsNullOrWhiteSpace(clientIdMetadataDocumentUri.Fragment) ||
            clientIdMetadataDocumentUri.IsFile)
        {
            return Fin.Fail<string>(Error.New("The configured client ID metadata document URI must be an absolute HTTPS URI without query or fragment components."));
        }

        return Fin.Succ(clientIdMetadataDocumentUri.AbsoluteUri);
    }

    private static async ValueTask<string> ReadResponseTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(McpOAuthAuthorizationCodeProvider));
        }
    }

    private sealed class McpOAuthAuthorizationState
    {
        public McpOAuthAuthorizationState(
            string authorizationServerIssuer,
            string resourceUri,
            string clientId,
            string accessToken,
            string? refreshToken,
            IReadOnlyList<string> grantedScopes,
            DateTimeOffset? expiresAtUtc)
        {
            AuthorizationServerIssuer = authorizationServerIssuer;
            ResourceUri = resourceUri;
            ClientId = clientId;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            GrantedScopes = grantedScopes;
            ExpiresAtUtc = expiresAtUtc;
        }

        public string AuthorizationServerIssuer { get; }

        public string ResourceUri { get; }

        public string ClientId { get; }

        public string AccessToken { get; }

        public string? RefreshToken { get; }

        public IReadOnlyList<string> GrantedScopes { get; }

        public DateTimeOffset? ExpiresAtUtc { get; }

        public bool Matches(McpAuthorizationServerDescriptor authorizationServer, string resourceUri)
        {
            return string.Equals(AuthorizationServerIssuer, authorizationServer.Issuer.AbsoluteUri, StringComparison.Ordinal) &&
                   string.Equals(ResourceUri, resourceUri, StringComparison.Ordinal);
        }

        public bool IsExpired(TimeSpan refreshSkew)
        {
            return ExpiresAtUtc is { } expiration && expiration <= DateTimeOffset.UtcNow.Add(refreshSkew);
        }
    }
}
