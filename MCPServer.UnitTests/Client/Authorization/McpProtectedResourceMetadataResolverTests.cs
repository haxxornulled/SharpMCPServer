using System.Net;
using System.Net.Http;
using System.Text;
using MCPServer.Client.Authorization;
using Xunit;

namespace MCPServer.UnitTests.Client.Authorization;

public sealed class McpProtectedResourceMetadataResolverTests
{
    [Fact]
    public async Task ResolveAsync_Uses_Challenge_Resource_Metadata_Uri_First()
    {
        var endpoint = new Uri("https://resource.example/api/v1");
        var challenge = new McpAuthorizationChallenge
        {
            ResourceMetadataUri = new Uri("https://resource.example/.well-known/oauth-protected-resource"),
        };

        var handler = new RecordingHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsoluteUri switch
            {
                "https://resource.example/.well-known/oauth-protected-resource" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"resource":"https://resource.example","authorization_servers":["https://auth.example"]}""",
                        Encoding.UTF8,
                        "application/json"),
                },
                "https://resource.example/.well-known/oauth-protected-resource/api/v1" => throw new InvalidOperationException("Fallback discovery must not run when challenge metadata is present."),
                _ => throw new InvalidOperationException($"Unexpected request URI: {request.RequestUri}"),
            };
        });

        using var httpClient = new HttpClient(handler);

        var result = await McpProtectedResourceMetadataResolver.ResolveAsync(httpClient, endpoint, challenge, CancellationToken.None);

        Assert.True(result.IsSucc);
        Assert.Equal(new[] { new Uri("https://resource.example/.well-known/oauth-protected-resource") }, handler.Requests);

        var metadata = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected protected resource metadata to resolve successfully."));

        Assert.Equal("https://resource.example", metadata.Resource);
        Assert.Equal(new[] { "https://auth.example" }, metadata.AuthorizationServers);
    }

    [Fact]
    public async Task ResolveAsync_Ignores_Off_Origin_Challenge_Metadata()
    {
        var endpoint = new Uri("https://resource.example/api/v1");
        var challenge = new McpAuthorizationChallenge
        {
            ResourceMetadataUri = new Uri("https://evil.example/metadata"),
        };

        var handler = new RecordingHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsoluteUri switch
            {
                "https://resource.example/.well-known/oauth-protected-resource/api/v1" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "https://resource.example/.well-known/oauth-protected-resource" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"resource":"https://resource.example","authorization_servers":["https://auth.example"]}""",
                        Encoding.UTF8,
                        "application/json"),
                },
                _ => throw new InvalidOperationException($"Unexpected request URI: {request.RequestUri}"),
            };
        });

        using var httpClient = new HttpClient(handler);

        var result = await McpProtectedResourceMetadataResolver.ResolveAsync(httpClient, endpoint, challenge, CancellationToken.None);

        Assert.True(result.IsSucc);
        Assert.Equal(
            new[]
            {
                new Uri("https://resource.example/.well-known/oauth-protected-resource/api/v1"),
                new Uri("https://resource.example/.well-known/oauth-protected-resource"),
            },
            handler.Requests);

        var metadata = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected protected resource metadata to resolve successfully."));

        Assert.Equal("https://resource.example", metadata.Resource);
        Assert.Equal(new[] { "https://auth.example" }, metadata.AuthorizationServers);
    }

    [Fact]
    public async Task ResolveAsync_Falls_Back_To_WellKnown_Uris_When_No_Challenge_Metadata_Is_Present()
    {
        var endpoint = new Uri("https://resource.example/api/v1");

        var handler = new RecordingHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsoluteUri switch
            {
                "https://resource.example/.well-known/oauth-protected-resource/api/v1" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "https://resource.example/.well-known/oauth-protected-resource" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"resource":"https://resource.example","authorization_servers":["https://auth.example"]}""",
                        Encoding.UTF8,
                        "application/json"),
                },
                _ => throw new InvalidOperationException($"Unexpected request URI: {request.RequestUri}"),
            };
        });

        using var httpClient = new HttpClient(handler);

        var result = await McpProtectedResourceMetadataResolver.ResolveAsync(httpClient, endpoint, challenge: null, CancellationToken.None);

        Assert.True(result.IsSucc);
        Assert.Equal(
            new[]
            {
                new Uri("https://resource.example/.well-known/oauth-protected-resource/api/v1"),
                new Uri("https://resource.example/.well-known/oauth-protected-resource"),
            },
            handler.Requests);

        var metadata = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected protected resource metadata to resolve successfully."));

        Assert.Equal("https://resource.example", metadata.Resource);
        Assert.Equal(new[] { "https://auth.example" }, metadata.AuthorizationServers);
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
