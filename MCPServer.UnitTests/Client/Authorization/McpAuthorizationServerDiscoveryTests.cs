using System.Net;
using System.Net.Http;
using System.Text;
using MCPServer.Client.Authorization;
using Xunit;

namespace MCPServer.UnitTests.Client.Authorization;

public sealed class McpAuthorizationServerDiscoveryTests
{
    [Fact]
    public async Task DiscoverAsync_Prefers_OAuth_Metadata_And_Requires_Pkce()
    {
        var issuer = new Uri("https://auth.example/tenant");

        var handler = new RecordingHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsoluteUri switch
            {
                "https://auth.example/.well-known/oauth-authorization-server/tenant" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"issuer":"https://auth.example/tenant","authorization_endpoint":"https://auth.example/authorize","token_endpoint":"https://auth.example/token","registration_endpoint":"https://auth.example/register","client_id_metadata_document_supported":true,"code_challenge_methods_supported":["S256","plain"]}""",
                        Encoding.UTF8,
                        "application/json"),
                },
                _ => throw new InvalidOperationException($"Unexpected request URI: {request.RequestUri}"),
            };
        });

        using var httpClient = new HttpClient(handler);

        var result = await McpAuthorizationServerDiscovery.DiscoverAsync(httpClient, issuer, CancellationToken.None);

        Assert.True(result.IsSucc);
        Assert.Equal(new[] { new Uri("https://auth.example/.well-known/oauth-authorization-server/tenant") }, handler.Requests);

        var metadata = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected authorization server metadata to resolve successfully."));

        Assert.Equal(McpAuthorizationServerDiscoverySource.OAuthAuthorizationServerMetadata, metadata.DiscoverySource);
        Assert.Equal(new Uri("https://auth.example/tenant"), metadata.Issuer);
        Assert.Equal(new Uri("https://auth.example/authorize"), metadata.AuthorizationEndpoint);
        Assert.Equal(new Uri("https://auth.example/token"), metadata.TokenEndpoint);
        Assert.Equal(new Uri("https://auth.example/register"), metadata.RegistrationEndpoint);
        Assert.True(metadata.ClientIdMetadataDocumentSupported);
        Assert.True(metadata.SupportsPkce);
    }

    [Fact]
    public async Task DiscoverAsync_Falls_Back_To_Oidc_Metadata_When_OAuth_Metadata_Is_Not_Found()
    {
        var issuer = new Uri("https://auth.example/tenant");

        var handler = new RecordingHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsoluteUri switch
            {
                "https://auth.example/.well-known/oauth-authorization-server/tenant" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "https://auth.example/.well-known/openid-configuration/tenant" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"issuer":"https://auth.example/tenant","authorization_endpoint":"https://auth.example/authorize","token_endpoint":"https://auth.example/token","code_challenge_methods_supported":["S256"]}""",
                        Encoding.UTF8,
                        "application/json"),
                },
                _ => throw new InvalidOperationException($"Unexpected request URI: {request.RequestUri}"),
            };
        });

        using var httpClient = new HttpClient(handler);

        var result = await McpAuthorizationServerDiscovery.DiscoverAsync(httpClient, issuer, CancellationToken.None);

        Assert.True(result.IsSucc);
        Assert.Equal(
            new[]
            {
                new Uri("https://auth.example/.well-known/oauth-authorization-server/tenant"),
                new Uri("https://auth.example/.well-known/openid-configuration/tenant"),
            },
            handler.Requests);

        var metadata = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected authorization server metadata to resolve successfully."));

        Assert.Equal(McpAuthorizationServerDiscoverySource.OpenIdConnectDiscovery, metadata.DiscoverySource);
        Assert.Equal(new Uri("https://auth.example/tenant"), metadata.Issuer);
        Assert.True(metadata.SupportsPkce);
        Assert.False(metadata.ClientIdMetadataDocumentSupported);
    }

    [Fact]
    public async Task DiscoverAsync_Rejects_Oidc_Metadata_Without_Pkce_Support()
    {
        var issuer = new Uri("https://auth.example/tenant");

        var handler = new RecordingHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsoluteUri switch
            {
                "https://auth.example/.well-known/oauth-authorization-server/tenant" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "https://auth.example/.well-known/openid-configuration/tenant" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"issuer":"https://auth.example/tenant","authorization_endpoint":"https://auth.example/authorize","token_endpoint":"https://auth.example/token"}""",
                        Encoding.UTF8,
                        "application/json"),
                },
                _ => throw new InvalidOperationException($"Unexpected request URI: {request.RequestUri}"),
            };
        });

        using var httpClient = new HttpClient(handler);

        var result = await McpAuthorizationServerDiscovery.DiscoverAsync(httpClient, issuer, CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Equal(
            new[]
            {
                new Uri("https://auth.example/.well-known/oauth-authorization-server/tenant"),
                new Uri("https://auth.example/.well-known/openid-configuration/tenant"),
            },
            handler.Requests);
    }

    [Fact]
    public async Task DiscoverAsync_Rejects_OAuth_Metadata_Without_Pkce_Support()
    {
        var issuer = new Uri("https://auth.example/tenant");

        var handler = new RecordingHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsoluteUri switch
            {
                "https://auth.example/.well-known/oauth-authorization-server/tenant" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"issuer":"https://auth.example/tenant","authorization_endpoint":"https://auth.example/authorize","token_endpoint":"https://auth.example/token"}""",
                        Encoding.UTF8,
                        "application/json"),
                },
                _ => throw new InvalidOperationException($"Unexpected request URI: {request.RequestUri}"),
            };
        });

        using var httpClient = new HttpClient(handler);

        var result = await McpAuthorizationServerDiscovery.DiscoverAsync(httpClient, issuer, CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Equal(new[] { new Uri("https://auth.example/.well-known/oauth-authorization-server/tenant") }, handler.Requests);
    }

    [Fact]
    public async Task DiscoverOidcMetadataAsync_FallsBackAfterNotFound()
    {
        var issuer = new Uri("https://auth.example/tenant");

        var handler = new RecordingHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsoluteUri switch
            {
                "https://auth.example/.well-known/openid-configuration/tenant" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "https://auth.example/tenant/.well-known/openid-configuration" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"issuer":"https://auth.example/tenant","authorization_endpoint":"https://auth.example/authorize","token_endpoint":"https://auth.example/token"}""",
                        Encoding.UTF8,
                        "application/json"),
                },
                _ => throw new InvalidOperationException($"Unexpected request URI: {request.RequestUri}"),
            };
        });

        using var httpClient = new HttpClient(handler);

        var result = await McpAuthorizationServerDiscovery.DiscoverOidcMetadataAsync(httpClient, issuer, CancellationToken.None);

        Assert.True(result.IsSucc);
        Assert.Equal(
            new[]
            {
                new Uri("https://auth.example/.well-known/openid-configuration/tenant"),
                new Uri("https://auth.example/tenant/.well-known/openid-configuration"),
            },
            handler.Requests);

        var metadata = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected OIDC discovery metadata to resolve successfully."));

        Assert.Equal("https://auth.example/tenant", metadata.Issuer);
        Assert.Equal("https://auth.example/authorize", metadata.AuthorizationEndpoint);
        Assert.Equal("https://auth.example/token", metadata.TokenEndpoint);
    }

    [Fact]
    public async Task DiscoverOAuthMetadataAsync_PreservesCancellation()
    {
        var issuer = new Uri("https://auth.example/tenant");
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"issuer":"https://auth.example/tenant","authorization_endpoint":"https://auth.example/authorize","token_endpoint":"https://auth.example/token"}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await McpAuthorizationServerDiscovery.DiscoverOAuthMetadataAsync(httpClient, issuer, cancellationTokenSource.Token));
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri ?? throw new InvalidOperationException("Request URI was missing.");
            Requests.Add(requestUri);
            return Task.FromResult(_responseFactory(request));
        }
    }
}
