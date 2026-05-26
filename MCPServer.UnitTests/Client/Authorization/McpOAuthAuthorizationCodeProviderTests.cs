using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LanguageExt;
using MCPServer.Client.Authorization;
using MCPServer.Client.Infrastructure.Authorization;
using Microsoft.Extensions.Http;
using Xunit;

namespace MCPServer.UnitTests.Client.Authorization;

public sealed class McpOAuthAuthorizationCodeProviderTests
{
    [Fact]
    public async Task GetAccessTokenAsync_Performs_Browser_Loopback_Auth_Code_Flow_And_Exchanges_Token()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokenEndpoint = new Uri("https://auth.example/token");
        var authorizationEndpoint = new Uri("https://auth.example/authorize");
        var descriptor = CreateAuthorizationServerDescriptor(authorizationEndpoint, tokenEndpoint);
        var handler = new OAuthFlowHttpMessageHandler(tokenEndpoint, registrationEndpoint: null, tokenResponseFactory: static form =>
        {
            Assert.Equal("authorization_code", form["grant_type"]);
            Assert.Equal("auth-code-123", form["code"]);
            Assert.Equal("https://resource.example/mcp", form["resource"]);
            Assert.Equal("mcp-client-123", form["client_id"]);
            Assert.False(string.IsNullOrWhiteSpace(form["code_verifier"]));
            Assert.Equal("files:read", form["scope"]);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"access-token-123","token_type":"Bearer","refresh_token":"refresh-token-123","expires_in":3600,"scope":"files:read"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        using var httpClientFactory = new RecordingHttpClientFactory(handler);
        using var browserLauncher = new RecordingBrowserLauncher();
        using var provider = new McpOAuthAuthorizationCodeProvider(
            new McpOAuthAuthorizationProviderOptions
            {
                ClientName = "MCP Test Client",
                ClientId = "mcp-client-123",
                UseDynamicClientRegistration = false,
                AuthorizationTimeout = TimeSpan.FromSeconds(10),
                TokenRequestTimeout = TimeSpan.FromSeconds(10),
                HttpClientName = "oauth-test-client"
            },
            httpClientFactory,
            browserLauncher);

        var providerTask = provider.GetAccessTokenAsync(CreateContext(descriptor, requiredScopes: new[] { "files:read" }), cancellationToken).AsTask();

        var launchedUri = await browserLauncher.LaunchedUriTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        var launchedQuery = ParseQueryString(launchedUri);
        Assert.Equal("code", launchedQuery["response_type"]);
        Assert.Equal("mcp-client-123", launchedQuery["client_id"]);
        Assert.Equal("https://resource.example/mcp", launchedQuery["resource"]);
        Assert.Equal("files:read", launchedQuery["scope"]);
        Assert.Equal("S256", launchedQuery["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(launchedQuery["code_challenge"]));
        Assert.False(string.IsNullOrWhiteSpace(launchedQuery["state"]));

        var redirectUri = new Uri(launchedQuery["redirect_uri"], UriKind.Absolute);
        await SendLoopbackCallbackAsync(redirectUri, "auth-code-123", launchedQuery["state"], cancellationToken);

        var accessTokenResult = await providerTask;
        Assert.True(accessTokenResult.IsSucc);
        Assert.Equal("access-token-123", accessTokenResult.Match(Succ: static value => value, Fail: static _ => throw new InvalidOperationException()));

        Assert.Equal(1, handler.TokenCallCount);
        Assert.Null(handler.RegistrationBody);

        var tokenForm = handler.LastTokenForm ?? throw new InvalidOperationException("Token form was not captured.");
        Assert.Equal("authorization_code", tokenForm["grant_type"]);
        Assert.Equal("auth-code-123", tokenForm["code"]);
        Assert.Equal("https://resource.example/mcp", tokenForm["resource"]);
        Assert.Equal("mcp-client-123", tokenForm["client_id"]);
        Assert.False(string.IsNullOrWhiteSpace(tokenForm["code_verifier"]));
        Assert.Equal("files:read", tokenForm["scope"]);
    }

    [Fact]
    public async Task GetAccessTokenAsync_Registers_Client_Dynamically_When_No_Client_Id_Is_Configured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokenEndpoint = new Uri("https://auth.example/token");
        var registrationEndpoint = new Uri("https://auth.example/register");
        var authorizationEndpoint = new Uri("https://auth.example/authorize");
        var descriptor = CreateAuthorizationServerDescriptor(authorizationEndpoint, tokenEndpoint, registrationEndpoint);
        var handler = new OAuthFlowHttpMessageHandler(tokenEndpoint, registrationEndpoint, tokenResponseFactory: static form =>
        {
            Assert.Equal("authorization_code", form["grant_type"]);
            Assert.Equal("dynamic-client-123", form["client_id"]);
            Assert.Equal("https://resource.example/mcp", form["resource"]);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"access-token-456","token_type":"Bearer","expires_in":3600,"scope":"files:read"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }, registrationResponseFactory: registrationDocument =>
        {
            Assert.Equal("MCP Dynamic Client", registrationDocument.RootElement.GetProperty("client_name").GetString());
            Assert.Equal("native", registrationDocument.RootElement.GetProperty("application_type").GetString());
            var grantTypes = registrationDocument.RootElement.GetProperty("grant_types").EnumerateArray().Select(static element => element.GetString()).ToArray();
            Assert.Equal(new[] { "authorization_code", "refresh_token" }, grantTypes);
            var responseTypes = registrationDocument.RootElement.GetProperty("response_types").EnumerateArray().Select(static element => element.GetString()).ToArray();
            Assert.Equal(new[] { "code" }, responseTypes);
            Assert.Equal("none", registrationDocument.RootElement.GetProperty("token_endpoint_auth_method").GetString());

            var redirectUris = registrationDocument.RootElement.GetProperty("redirect_uris").EnumerateArray().Select(static element => element.GetString()).ToArray();
            Assert.Single(redirectUris);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"client_id":"dynamic-client-123"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        using var httpClientFactory = new RecordingHttpClientFactory(handler);
        using var browserLauncher = new RecordingBrowserLauncher();
        using var provider = new McpOAuthAuthorizationCodeProvider(
            new McpOAuthAuthorizationProviderOptions
            {
                ClientName = "MCP Dynamic Client",
                UseDynamicClientRegistration = true,
                AuthorizationTimeout = TimeSpan.FromSeconds(10),
                TokenRequestTimeout = TimeSpan.FromSeconds(10),
                HttpClientName = "oauth-test-client"
            },
            httpClientFactory,
            browserLauncher);

        var providerTask = provider.GetAccessTokenAsync(CreateContext(descriptor, requiredScopes: new[] { "files:read" }), cancellationToken).AsTask();

        var launchedUri = await browserLauncher.LaunchedUriTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        var launchedQuery = ParseQueryString(launchedUri);
        Assert.Equal("dynamic-client-123", launchedQuery["client_id"]);
        Assert.Equal("https://resource.example/mcp", launchedQuery["resource"]);

        var redirectUri = new Uri(launchedQuery["redirect_uri"], UriKind.Absolute);
        await SendLoopbackCallbackAsync(redirectUri, "auth-code-456", launchedQuery["state"], cancellationToken);

        var accessTokenResult = await providerTask;
        Assert.True(accessTokenResult.IsSucc);
        Assert.Equal("access-token-456", accessTokenResult.Match(Succ: static value => value, Fail: static _ => throw new InvalidOperationException()));

        Assert.Equal(1, handler.RegistrationCallCount);
        Assert.NotNull(handler.RegistrationBody);
    }

    [Fact]
    public async Task GetAccessTokenAsync_Uses_Client_Id_Metadata_Document_When_Configured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokenEndpoint = new Uri("https://auth.example/token");
        var authorizationEndpoint = new Uri("https://auth.example/authorize");
        var descriptor = CreateAuthorizationServerDescriptor(authorizationEndpoint, tokenEndpoint, clientIdMetadataDocumentSupported: true);
        var clientIdMetadataDocumentUri = new Uri("https://client.example/metadata.json");
        var handler = new OAuthFlowHttpMessageHandler(tokenEndpoint, registrationEndpoint: null, tokenResponseFactory: form =>
        {
            Assert.Equal(clientIdMetadataDocumentUri.AbsoluteUri, form["client_id"]);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"access-token-789","token_type":"Bearer","expires_in":3600}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        using var httpClientFactory = new RecordingHttpClientFactory(handler);
        using var browserLauncher = new RecordingBrowserLauncher();
        using var provider = new McpOAuthAuthorizationCodeProvider(
            new McpOAuthAuthorizationProviderOptions
            {
                ClientName = "MCP Client Metadata",
                ClientIdMetadataDocumentUri = clientIdMetadataDocumentUri,
                UseDynamicClientRegistration = false,
                AuthorizationTimeout = TimeSpan.FromSeconds(10),
                TokenRequestTimeout = TimeSpan.FromSeconds(10),
                HttpClientName = "oauth-test-client"
            },
            httpClientFactory,
            browserLauncher);

        var providerTask = provider.GetAccessTokenAsync(CreateContext(descriptor, requiredScopes: Array.Empty<string>()), cancellationToken).AsTask();

        var launchedUri = await browserLauncher.LaunchedUriTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        var launchedQuery = ParseQueryString(launchedUri);
        Assert.Equal(clientIdMetadataDocumentUri.AbsoluteUri, launchedQuery["client_id"]);
        Assert.Equal("https://resource.example/mcp", launchedQuery["resource"]);

        var redirectUri = new Uri(launchedQuery["redirect_uri"], UriKind.Absolute);
        await SendLoopbackCallbackAsync(redirectUri, "auth-code-789", launchedQuery["state"], cancellationToken);

        var accessTokenResult = await providerTask;
        Assert.True(accessTokenResult.IsSucc);
        Assert.Equal("access-token-789", accessTokenResult.Match(Succ: static value => value, Fail: static _ => throw new InvalidOperationException()));

        Assert.Equal(0, handler.RegistrationCallCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_Refreshes_Cached_Token_When_A_Challenge_Reoccurs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tokenEndpoint = new Uri("https://auth.example/token");
        var authorizationEndpoint = new Uri("https://auth.example/authorize");
        var descriptor = CreateAuthorizationServerDescriptor(authorizationEndpoint, tokenEndpoint);
        var tokenRequestCount = 0;
        var handler = new OAuthFlowHttpMessageHandler(tokenEndpoint, registrationEndpoint: null, tokenResponseFactory: form =>
        {
            tokenRequestCount++;
            if (tokenRequestCount == 1)
            {
                Assert.Equal("authorization_code", form["grant_type"]);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"access-token-001","token_type":"Bearer","refresh_token":"refresh-token-001","expires_in":3600,"scope":"files:read"}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            Assert.Equal("refresh_token", form["grant_type"]);
            Assert.Equal("refresh-token-001", form["refresh_token"]);
            Assert.Equal("mcp-client-001", form["client_id"]);
            Assert.Equal("https://resource.example/mcp", form["resource"]);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"access-token-002","token_type":"Bearer","expires_in":3600,"scope":"files:read"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        using var httpClientFactory = new RecordingHttpClientFactory(handler);
        using var browserLauncher = new RecordingBrowserLauncher();
        using var provider = new McpOAuthAuthorizationCodeProvider(
            new McpOAuthAuthorizationProviderOptions
            {
                ClientName = "MCP Refresh Client",
                ClientId = "mcp-client-001",
                UseDynamicClientRegistration = false,
                AuthorizationTimeout = TimeSpan.FromSeconds(10),
                TokenRequestTimeout = TimeSpan.FromSeconds(10),
                HttpClientName = "oauth-test-client"
            },
            httpClientFactory,
            browserLauncher);

        var firstCallTask = provider.GetAccessTokenAsync(CreateContext(descriptor, requiredScopes: new[] { "files:read" }), cancellationToken).AsTask();
        var firstLaunchUri = await browserLauncher.LaunchedUriTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        var firstLaunchQuery = ParseQueryString(firstLaunchUri);
        await SendLoopbackCallbackAsync(new Uri(firstLaunchQuery["redirect_uri"], UriKind.Absolute), "auth-code-001", firstLaunchQuery["state"], cancellationToken);

        var firstAccessTokenResult = await firstCallTask;
        Assert.True(firstAccessTokenResult.IsSucc);
        Assert.Equal("access-token-001", firstAccessTokenResult.Match(Succ: static value => value, Fail: static _ => throw new InvalidOperationException()));

        var secondContext = new McpAuthorizationContext
        {
            Endpoint = new Uri("https://resource.example/mcp"),
            AuthorizationServers = new[] { descriptor },
            RequiredScopes = new[] { "files:read" },
            Challenge = new McpAuthorizationChallenge
            {
                Scope = "files:read"
            }
        };

        var secondAccessTokenResult = await provider.GetAccessTokenAsync(secondContext, cancellationToken);
        Assert.True(secondAccessTokenResult.IsSucc);
        Assert.Equal("access-token-002", secondAccessTokenResult.Match(Succ: static value => value, Fail: static _ => throw new InvalidOperationException()));
        Assert.Equal(1, browserLauncher.CallCount);
        Assert.Equal(2, handler.TokenCallCount);
    }

    private static McpAuthorizationContext CreateContext(
        McpAuthorizationServerDescriptor descriptor,
        IReadOnlyList<string> requiredScopes)
    {
        return new McpAuthorizationContext
        {
            Endpoint = new Uri("https://resource.example/mcp"),
            AuthorizationServers = new[] { descriptor },
            RequiredScopes = requiredScopes
        };
    }

    private static McpAuthorizationServerDescriptor CreateAuthorizationServerDescriptor(
        Uri authorizationEndpoint,
        Uri tokenEndpoint,
        Uri? registrationEndpoint = null,
        bool clientIdMetadataDocumentSupported = false)
    {
        return new McpAuthorizationServerDescriptor
        {
            MetadataUri = new Uri("https://auth.example/.well-known/oauth-authorization-server"),
            Issuer = new Uri("https://auth.example/tenant"),
            AuthorizationEndpoint = authorizationEndpoint,
            TokenEndpoint = tokenEndpoint,
            RegistrationEndpoint = registrationEndpoint,
            ClientIdMetadataDocumentSupported = clientIdMetadataDocumentSupported,
            CodeChallengeMethodsSupported = new[] { "S256" },
            DiscoverySource = McpAuthorizationServerDiscoverySource.OAuthAuthorizationServerMetadata
        };
    }

    private static async Task SendLoopbackCallbackAsync(Uri redirectUri, string code, string state, CancellationToken cancellationToken)
    {
        var callbackUri = new UriBuilder(redirectUri)
        {
            Query = $"code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}"
        }.Uri;

        using var httpClient = new HttpClient(new HttpClientHandler
        {
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var response = await httpClient.GetAsync(callbackUri, cancellationToken);
        var _ = await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static Dictionary<string, string> ParseQueryString(Uri uri)
    {
        var query = uri.Query.TrimStart('?');
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = pair.IndexOf('=');
            var name = equalsIndex >= 0 ? pair[..equalsIndex] : pair;
            var value = equalsIndex >= 0 ? pair[(equalsIndex + 1)..] : string.Empty;
            result[name] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return result;
    }

    private sealed class RecordingBrowserLauncher : IMcpBrowserLauncher, IDisposable, IAsyncDisposable
    {
        private readonly TaskCompletionSource<Uri> _launchTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;

        public Uri? LaunchedUri { get; private set; }

        public int CallCount { get; private set; }

        public Task<Uri> LaunchedUriTask => _launchTcs.Task;

        public bool TryLaunch(Uri uri, out string? errorMessage)
        {
            CallCount++;
            LaunchedUri = uri;
            errorMessage = null;
            _launchTcs.TrySetResult(uri);
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly HttpMessageHandler _handler;
        private bool _disposed;

        public RecordingHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }
    }

    private sealed class OAuthFlowHttpMessageHandler : HttpMessageHandler
    {
        private readonly Uri _tokenEndpoint;
        private readonly Uri? _registrationEndpoint;
        private readonly Func<IReadOnlyDictionary<string, string>, HttpResponseMessage> _tokenResponseFactory;
        private readonly Func<JsonDocument, HttpResponseMessage>? _registrationResponseFactory;

        public OAuthFlowHttpMessageHandler(
            Uri tokenEndpoint,
            Uri? registrationEndpoint,
            Func<IReadOnlyDictionary<string, string>, HttpResponseMessage> tokenResponseFactory,
            Func<JsonDocument, HttpResponseMessage>? registrationResponseFactory = null)
        {
            _tokenEndpoint = tokenEndpoint;
            _registrationEndpoint = registrationEndpoint;
            _tokenResponseFactory = tokenResponseFactory;
            _registrationResponseFactory = registrationResponseFactory;
        }

        public int RegistrationCallCount { get; private set; }

        public int TokenCallCount { get; private set; }

        public string? RegistrationBody { get; private set; }

        public string? LastTokenBody { get; private set; }

        public IReadOnlyDictionary<string, string>? LastTokenForm { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri ?? throw new InvalidOperationException("Request URI was missing.");

            if (_registrationEndpoint is not null && request.Method == HttpMethod.Post && requestUri.AbsoluteUri == _registrationEndpoint.AbsoluteUri)
            {
                RegistrationCallCount++;
                RegistrationBody = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                if (_registrationResponseFactory is null)
                {
                    throw new InvalidOperationException("Registration was not expected in this test.");
                }

                using var registrationDocument = JsonDocument.Parse(RegistrationBody);
                return _registrationResponseFactory(registrationDocument);
            }

            if (request.Method == HttpMethod.Post && requestUri.AbsoluteUri == _tokenEndpoint.AbsoluteUri)
            {
                TokenCallCount++;
                LastTokenBody = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                LastTokenForm = ParseForm(LastTokenBody);
                return _tokenResponseFactory(LastTokenForm);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {requestUri.AbsoluteUri}");
        }

        private static IReadOnlyDictionary<string, string> ParseForm(string value)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            foreach (var part in value.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equalsIndex = part.IndexOf('=');
                var name = equalsIndex >= 0 ? part[..equalsIndex] : part;
                var rawValue = equalsIndex >= 0 ? part[(equalsIndex + 1)..] : string.Empty;
                result[Uri.UnescapeDataString(name.Replace('+', ' '))] = Uri.UnescapeDataString(rawValue.Replace('+', ' '));
            }

            return result;
        }
    }
}
