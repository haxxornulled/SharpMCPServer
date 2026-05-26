using System.Net;
using System.Net.Http.Headers;
using LanguageExt;
using MCPServer.Domain.Mcp;
using Microsoft.Extensions.Logging;

namespace MCPServer.Infrastructure.Mcp.Http.Authorization;

public sealed class McpHttpAuthorizationService : IMcpHttpAuthorizationService
{
    private readonly StreamableHttpMcpTransportOptions _options;
    private readonly IMcpProtectedResourceMetadataProvider _metadataProvider;
    private readonly IMcpAccessTokenValidator _tokenValidator;
    private readonly ILogger<McpHttpAuthorizationService> _logger;

    public McpHttpAuthorizationService(
        StreamableHttpMcpTransportOptions options,
        IMcpProtectedResourceMetadataProvider metadataProvider,
        IMcpAccessTokenValidator tokenValidator,
        ILogger<McpHttpAuthorizationService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadataProvider);
        ArgumentNullException.ThrowIfNull(tokenValidator);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _metadataProvider = metadataProvider;
        _tokenValidator = tokenValidator;
        _logger = logger;
    }

    public async ValueTask<McpHttpAuthorizationDecision> AuthorizeAsync(StreamableHttpMcpRequestEnvelope request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.Authorization.Enabled)
        {
            return McpHttpAuthorizationDecision.Authorized();
        }

        var resourceMetadataUri = _metadataProvider.GetResourceMetadataUri(request.RequestUri);
        var requiredScopes = GetRequiredScopes();

        var authorizationHeader = request.GetHeader(StreamableHttpMcpHeaderNames.Authorization);
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return McpHttpAuthorizationDecision.Denied(
                HttpStatusCode.Unauthorized,
                McpBearerChallengeBuilder.Build(resourceMetadataUri, requiredScopes),
                "The MCP request did not include an access token.");
        }

        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var parsedAuthorization) ||
            !string.Equals(parsedAuthorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(parsedAuthorization.Parameter))
        {
            return McpHttpAuthorizationDecision.Denied(
                HttpStatusCode.Unauthorized,
                McpBearerChallengeBuilder.Build(resourceMetadataUri, requiredScopes, error: "invalid_token", errorDescription: "The Authorization header did not contain a valid Bearer token."),
                "The MCP request included an invalid Authorization header.");
        }

        var tokenResult = await _tokenValidator.ValidateAsync(parsedAuthorization.Parameter.Trim(), BuildResourceUri(request.RequestUri), cancellationToken).ConfigureAwait(false);
        if (tokenResult.IsFail)
        {
            var errorMessage = tokenResult.Match(Succ: static _ => string.Empty, Fail: static failure => failure.Message);
            _logger.LogWarning("Rejected MCP bearer token for {RequestUri}: {ErrorMessage}", request.RequestUri, errorMessage);
            return McpHttpAuthorizationDecision.Denied(
                HttpStatusCode.Unauthorized,
                McpBearerChallengeBuilder.Build(resourceMetadataUri, requiredScopes, error: "invalid_token", errorDescription: "The supplied access token is invalid or expired."),
                "The supplied access token was rejected.");
        }

        var token = tokenResult.Match(Succ: static value => value, Fail: static _ => throw new InvalidOperationException());
        if (!HasRequiredScopes(token.Scopes, requiredScopes))
        {
            return McpHttpAuthorizationDecision.Denied(
                HttpStatusCode.Forbidden,
                McpBearerChallengeBuilder.Build(
                    resourceMetadataUri,
                    requiredScopes,
                    error: "insufficient_scope",
                    errorDescription: "The access token does not carry the scopes required for this MCP operation."),
                "The supplied access token does not include the required scopes.");
        }

        return McpHttpAuthorizationDecision.Authorized();
    }

    private Uri BuildResourceUri(Uri requestUri)
    {
        var normalizedPath = StreamableHttpMcpTransportOptions.NormalizePath(_options.Path);
        return new Uri(requestUri.GetLeftPart(UriPartial.Authority) + normalizedPath);
    }

    private string[] GetRequiredScopes()
    {
        if (_options.Authorization.RequiredScopes.Count == 0)
        {
            return Array.Empty<string>();
        }

        return _options.Authorization.RequiredScopes
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasRequiredScopes(IReadOnlyCollection<string> tokenScopes, IReadOnlyCollection<string> requiredScopes)
    {
        if (requiredScopes.Count == 0)
        {
            return true;
        }

        foreach (var requiredScope in requiredScopes)
        {
            var hasScope = false;
            foreach (var tokenScope in tokenScopes)
            {
                if (string.Equals(tokenScope, requiredScope, StringComparison.Ordinal))
                {
                    hasScope = true;
                    break;
                }
            }

            if (!hasScope)
            {
                return false;
            }
        }

        return true;
    }

}
