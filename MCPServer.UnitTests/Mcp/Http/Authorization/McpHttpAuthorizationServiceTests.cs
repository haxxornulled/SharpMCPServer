using System.Net;
using LanguageExt;
using MCPServer.Infrastructure.Mcp.Http;
using MCPServer.Infrastructure.Mcp.Http.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPServer.UnitTests.Mcp.Http.Authorization;

public sealed class McpHttpAuthorizationServiceTests
{
    [Fact]
    public async Task AuthorizeAsync_Returns_401_And_Challenge_When_Authorization_Is_Missing()
    {
        var options = BuildOptions(new List<string> { "files:read" });
        var provider = new McpProtectedResourceMetadataProvider(options);
        var service = new McpHttpAuthorizationService(options, provider, new StubAccessTokenValidator(Fin.Fail<McpAccessTokenValidationResult>(LanguageExt.Common.Error.New("not used"))), NullLogger<McpHttpAuthorizationService>.Instance);

        var decision = await service.AuthorizeAsync(
            new StreamableHttpMcpRequestEnvelope
            {
                Method = "GET",
                RequestUri = new Uri("http://127.0.0.1:8080/mcp/"),
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            },
            CancellationToken.None);

        Assert.False(decision.IsAuthorized);
        Assert.Equal(HttpStatusCode.Unauthorized, decision.StatusCode);
        Assert.Contains("resource_metadata=\"http://127.0.0.1:8080/.well-known/oauth-protected-resource\"", decision.WwwAuthenticate, StringComparison.Ordinal);
        Assert.Contains("scope=\"files:read\"", decision.WwwAuthenticate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizeAsync_Returns_403_And_Challenge_When_Scopes_Are_Insufficient()
    {
        var options = BuildOptions(new List<string> { "files:read", "files:write" });
        Assert.Equal(2, options.Authorization.RequiredScopes.Count);
        var provider = new McpProtectedResourceMetadataProvider(options);
        var validator = new StubAccessTokenValidator(
            Fin.Succ(new McpAccessTokenValidationResult
            {
                Principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()),
                Issuer = "https://auth.example.com/",
                Scopes = new List<string> { "files:read" }
            }));
        var service = new McpHttpAuthorizationService(options, provider, validator, NullLogger<McpHttpAuthorizationService>.Instance);

        var decision = await service.AuthorizeAsync(
            new StreamableHttpMcpRequestEnvelope
            {
                Method = "GET",
                RequestUri = new Uri("http://127.0.0.1:8080/mcp/"),
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [StreamableHttpMcpHeaderNames.Authorization] = "Bearer token"
                }
            },
            CancellationToken.None);

        Assert.False(decision.IsAuthorized);
        Assert.Equal(HttpStatusCode.Forbidden, decision.StatusCode);
        var expectedChallenge = McpBearerChallengeBuilder.Build(
            new Uri("http://127.0.0.1:8080/.well-known/oauth-protected-resource"),
            new[] { "files:read", "files:write" },
            error: "insufficient_scope",
            errorDescription: "The access token does not carry the scopes required for this MCP operation.");

        Assert.Equal(expectedChallenge, decision.WwwAuthenticate);
    }

    [Fact]
    public async Task AuthorizeAsync_Allows_Valid_Bearer_Tokens_With_Required_Scopes()
    {
        var options = BuildOptions(new List<string> { "files:read" });
        var provider = new McpProtectedResourceMetadataProvider(options);
        var validator = new StubAccessTokenValidator(
            Fin.Succ(new McpAccessTokenValidationResult
            {
                Principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()),
                Issuer = "https://auth.example.com/",
                Scopes = new List<string> { "files:read", "files:write" }
            }));
        var service = new McpHttpAuthorizationService(options, provider, validator, NullLogger<McpHttpAuthorizationService>.Instance);

        var decision = await service.AuthorizeAsync(
            new StreamableHttpMcpRequestEnvelope
            {
                Method = "GET",
                RequestUri = new Uri("http://127.0.0.1:8080/mcp/"),
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [StreamableHttpMcpHeaderNames.Authorization] = "Bearer token"
                }
            },
            CancellationToken.None);

        Assert.True(decision.IsAuthorized);
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        Assert.Null(decision.WwwAuthenticate);
    }

    private static StreamableHttpMcpTransportOptions BuildOptions(IReadOnlyList<string> requiredScopes)
    {
        return new StreamableHttpMcpTransportOptions
        {
            Path = "/mcp/",
            Authorization = new StreamableHttpMcpAuthorizationOptions
            {
                Enabled = true,
                AuthorizationServers = new List<string> { "https://auth.example.com/" },
                RequiredScopes = requiredScopes,
                ScopesSupported = new List<string> { "files:read", "files:write" }
            }
        };
    }

    private sealed class StubAccessTokenValidator : IMcpAccessTokenValidator
    {
        private readonly Fin<McpAccessTokenValidationResult> _result;

        public StubAccessTokenValidator(Fin<McpAccessTokenValidationResult> result)
        {
            _result = result;
        }

        public ValueTask<Fin<McpAccessTokenValidationResult>> ValidateAsync(string accessToken, Uri resourceUri, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
