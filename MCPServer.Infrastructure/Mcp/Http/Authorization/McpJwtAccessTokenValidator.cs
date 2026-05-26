using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public sealed class McpJwtAccessTokenValidator : IMcpAccessTokenValidator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly StreamableHttpMcpAuthorizationOptions _authorizationOptions;
    private readonly ILogger<McpJwtAccessTokenValidator> _logger;
    private readonly ConcurrentDictionary<string, Task<McpValidationConfiguration>> _configurationCache = new(StringComparer.OrdinalIgnoreCase);

    public McpJwtAccessTokenValidator(
        IHttpClientFactory httpClientFactory,
        StreamableHttpMcpTransportOptions transportOptions,
        ILogger<McpJwtAccessTokenValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(transportOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _authorizationOptions = transportOptions.Authorization;
        _logger = logger;
    }

    public async ValueTask<Fin<McpAccessTokenValidationResult>> ValidateAsync(string accessToken, Uri resourceUri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(resourceUri);

        var authorizationServers = GetAuthorizationServers();
        if (authorizationServers.Length == 0)
        {
            return Fin.Fail<McpAccessTokenValidationResult>(Error.New("Streamable HTTP authorization is enabled but no authorization servers were configured."));
        }

        var handler = new JwtSecurityTokenHandler();
        Exception? lastException = null;

        foreach (var authorizationServer in authorizationServers)
        {
            try
            {
                var configuration = await GetConfigurationAsync(authorizationServer, cancellationToken).ConfigureAwait(false);
                if (!TryValidateToken(handler, accessToken, resourceUri, configuration, out var validationResult, out var validationError))
                {
                    lastException = validationError;
                    continue;
                }

                return Fin.Succ(validationResult);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _configurationCache.TryRemove(authorizationServer.AbsoluteUri, out _);
                _logger.LogDebug(ex, "Failed to validate MCP access token against authorization server {AuthorizationServer}.", authorizationServer);
            }
        }

        var errorMessage = lastException is null
            ? "Unable to validate the supplied MCP access token."
            : $"Unable to validate the supplied MCP access token: {lastException.Message}";

        return Fin.Fail<McpAccessTokenValidationResult>(Error.New(errorMessage));
    }

    private async ValueTask<McpValidationConfiguration> GetConfigurationAsync(Uri authorizationServer, CancellationToken cancellationToken)
    {
        var cacheKey = authorizationServer.AbsoluteUri;
        var task = _configurationCache.GetOrAdd(cacheKey, _ => LoadConfigurationAsync(authorizationServer, cancellationToken));

        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            _configurationCache.TryRemove(cacheKey, out _);
            throw;
        }
    }

    private async Task<McpValidationConfiguration> LoadConfigurationAsync(Uri authorizationServer, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("MCPServer.AuthorizationDiscovery");
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        var discoveryDocument = await DiscoverOidcConfigurationAsync(httpClient, authorizationServer, cancellationToken).ConfigureAwait(false);
        var jwksUri = discoveryDocument.JwksUri;
        if (string.IsNullOrWhiteSpace(jwksUri) || !Uri.TryCreate(jwksUri, UriKind.Absolute, out var parsedJwksUri))
        {
            throw new InvalidOperationException($"The authorization server '{authorizationServer}' did not publish a valid jwks_uri.");
        }

        var jwksJson = await httpClient.GetStringAsync(parsedJwksUri, cancellationToken).ConfigureAwait(false);
        var jsonWebKeySet = new JsonWebKeySet(jwksJson);
        var signingKeys = jsonWebKeySet.GetSigningKeys();
        if (signingKeys.Count == 0)
        {
            throw new InvalidOperationException($"The authorization server '{authorizationServer}' did not publish any signing keys.");
        }

        var issuer = !string.IsNullOrWhiteSpace(discoveryDocument.Issuer)
            ? discoveryDocument.Issuer.Trim()
            : authorizationServer.AbsoluteUri;

        return new McpValidationConfiguration(issuer, signingKeys.ToArray());
    }

    private async ValueTask<McpOpenIdConnectDiscoveryDocument> DiscoverOidcConfigurationAsync(HttpClient httpClient, Uri issuer, CancellationToken cancellationToken)
    {
        foreach (var candidate in BuildOidcMetadataCandidates(issuer))
        {
            try
            {
                var document = await httpClient.GetFromJsonAsync(candidate, McpHttpAuthorizationJsonSerializerContext.Default.McpOpenIdConnectDiscoveryDocument, cancellationToken).ConfigureAwait(false);
                if (document is not null)
                {
                    return document;
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

        throw new InvalidOperationException($"Unable to resolve OpenID Connect discovery metadata for issuer '{issuer}'.");
    }

    private static IEnumerable<Uri> BuildOidcMetadataCandidates(Uri issuer)
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

    private bool TryValidateToken(
        JwtSecurityTokenHandler handler,
        string accessToken,
        Uri resourceUri,
        McpValidationConfiguration configuration,
        out McpAccessTokenValidationResult validationResult,
        out Exception? error)
    {
        validationResult = default!;
        error = null;

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration.Issuer,
            ValidateAudience = true,
            ValidAudience = resourceUri.AbsoluteUri,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = "scope"
        };

        try
        {
            var principal = handler.ValidateToken(accessToken, validationParameters, out var securityToken);
            if (securityToken is not JwtSecurityToken)
            {
                error = new SecurityTokenException("The access token was not a JWT.");
                return false;
            }

            validationResult = new McpAccessTokenValidationResult
            {
                Principal = principal,
                Issuer = configuration.Issuer,
                Scopes = ReadScopes(principal)
            };
            return true;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or InvalidOperationException)
        {
            error = ex;
            return false;
        }
    }

    private Uri[] GetAuthorizationServers()
    {
        return _authorizationOptions.AuthorizationServers
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static value => value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/")
            .Select(static value => new Uri(value, UriKind.Absolute))
            .ToArray();
    }

    private static IReadOnlyList<string> ReadScopes(ClaimsPrincipal principal)
    {
        var scopes = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in principal.Claims)
        {
            if (!string.Equals(claim.Type, "scope", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(claim.Type, "scp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var scope in values)
            {
                scopes.Add(scope);
            }
        }

        return scopes.ToArray();
    }

    private sealed record McpValidationConfiguration(string Issuer, SecurityKey[] SigningKeys);
}
