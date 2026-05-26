using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LanguageExt;
using MCPServer.Client.Authorization;
using MCPServer.Client.Interfaces;
using MCPServer.Client.Models;
using MCPServer.Client.StreamableHttp;
using MCPServer.Domain.Mcp;
using Microsoft.Extensions.Http;
using Xunit;

namespace MCPServer.UnitTests.Client.StreamableHttp;

public sealed class StreamableHttpMcpClientSessionTests
{
    [Fact]
    public async Task Initialize_And_Subsequent_Request_Posts_Send_Required_Mcp_Headers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new RecordingHttpMessageHandler();
        var options = new McpStreamableHttpClientOptions
        {
            Endpoint = new Uri("https://resource.example/mcp"),
            HttpMessageHandler = handler,
            OpenServerEventStream = true
        };

        var sessionResult = await StreamableHttpMcpClientSession.StartAsync(options, cancellationToken: cancellationToken);
        Assert.True(sessionResult.IsSucc);

        var session = sessionResult.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var initializeResult = await session.InitializeAsync(cancellationToken);
        Assert.True(initializeResult.IsSucc);

        var initialize = initializeResult.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(McpProtocolVersions.Current, initialize.ProtocolVersion);

        var getRequest = await handler.GetEventStreamRequestAsync().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.Equal(HttpMethod.Get, getRequest.HttpMethod);
        Assert.Equal(new[] { "text/event-stream" }, getRequest.AcceptValues);
        Assert.Equal(McpProtocolVersions.Current, getRequest.GetHeader("MCP-Protocol-Version"));
        Assert.Equal("session-123", getRequest.GetHeader("MCP-Session-Id"));

        var toolsResult = await session.ListToolsAsync(null, cancellationToken);
        Assert.True(toolsResult.IsSucc);

        var tools = toolsResult.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Single(tools.Tools);
        Assert.Equal("execute_sql", tools.Tools[0].Name);

        using var argumentsDocument = JsonDocument.Parse("""{"region":"Hello, 世界","query":"SELECT * FROM users"}""");
        var callResult = await session.CallToolAsync("execute_sql", argumentsDocument.RootElement.Clone(), cancellationToken);
        Assert.True(callResult.IsSucc);

        var initializeRequest = handler.FindSingleRequest("initialize");
        Assert.Equal(HttpMethod.Post, initializeRequest.HttpMethod);
        Assert.Equal(McpMethods.Initialize, initializeRequest.GetHeader("Mcp-Method"));
        Assert.Null(initializeRequest.GetHeader("MCP-Protocol-Version"));
        Assert.Contains("application/json", initializeRequest.AcceptValues);
        Assert.Contains("text/event-stream", initializeRequest.AcceptValues);

        var initializedNotification = handler.FindSingleRequest(McpMethods.NotificationsInitialized);
        Assert.Equal(McpMethods.NotificationsInitialized, initializedNotification.GetHeader("Mcp-Method"));
        Assert.Equal(McpProtocolVersions.Current, initializedNotification.GetHeader("MCP-Protocol-Version"));
        Assert.Equal("session-123", initializedNotification.GetHeader("MCP-Session-Id"));
        Assert.Contains("application/json", initializedNotification.AcceptValues);
        Assert.Contains("text/event-stream", initializedNotification.AcceptValues);

        var toolsListRequest = handler.FindSingleRequest(McpMethods.ToolsList);
        Assert.Equal(McpMethods.ToolsList, toolsListRequest.GetHeader("Mcp-Method"));
        Assert.Equal(McpProtocolVersions.Current, toolsListRequest.GetHeader("MCP-Protocol-Version"));
        Assert.Equal("session-123", toolsListRequest.GetHeader("MCP-Session-Id"));
        Assert.Contains("application/json", toolsListRequest.AcceptValues);
        Assert.Contains("text/event-stream", toolsListRequest.AcceptValues);

        var toolsCallRequest = handler.FindSingleRequest(McpMethods.ToolsCall);
        Assert.Equal(McpMethods.ToolsCall, toolsCallRequest.GetHeader("Mcp-Method"));
        Assert.Equal("execute_sql", toolsCallRequest.GetHeader("Mcp-Name"));
        Assert.Equal(McpProtocolVersions.Current, toolsCallRequest.GetHeader("MCP-Protocol-Version"));
        Assert.Equal("session-123", toolsCallRequest.GetHeader("MCP-Session-Id"));
        Assert.Equal("=?base64?SGVsbG8sIOS4lueVjA==?=", toolsCallRequest.GetHeader("Mcp-Param-Region"));
        Assert.Contains("application/json", toolsCallRequest.AcceptValues);
        Assert.Contains("text/event-stream", toolsCallRequest.AcceptValues);

        await session.DisposeAsync();

        var deleteRequest = handler.FindSingleRequest(HttpMethod.Delete);
        Assert.Equal("session-123", deleteRequest.GetHeader("MCP-Session-Id"));
        Assert.Equal(McpProtocolVersions.Current, deleteRequest.GetHeader("MCP-Protocol-Version"));
    }

    [Fact]
    public async Task StartAsync_Uses_Provided_HttpClientFactory_Before_Handler_Fallback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factoryHandler = new RecordingHttpMessageHandler();
        using var fallbackHandler = new ThrowingHttpMessageHandler();
        var httpClientFactory = new RecordingHttpClientFactory(factoryHandler);
        var options = new McpStreamableHttpClientOptions
        {
            Endpoint = new Uri("https://resource.example/mcp"),
            HttpClientFactory = httpClientFactory,
            HttpClientName = "mcpserver-client-console-http",
            HttpMessageHandler = fallbackHandler,
            OpenServerEventStream = false
        };

        var sessionResult = await StreamableHttpMcpClientSession.StartAsync(options, cancellationToken: cancellationToken);
        Assert.True(sessionResult.IsSucc);
        Assert.Equal(1, httpClientFactory.CreateClientCallCount);
        Assert.Equal("mcpserver-client-console-http", httpClientFactory.LastClientName);

        var session = sessionResult.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var initializeResult = await session.InitializeAsync(cancellationToken);
        Assert.True(initializeResult.IsSucc);

        var toolsResult = await session.ListToolsAsync(null, cancellationToken);
        Assert.True(toolsResult.IsSucc);

        await session.DisposeAsync();

        var deleteRequest = factoryHandler.FindSingleRequest(HttpMethod.Delete);
        Assert.Equal("session-123", deleteRequest.GetHeader("MCP-Session-Id"));
    }

    [Fact]
    public async Task Initialize_Rejects_Unsupported_Server_Protocol_Version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new RecordingHttpMessageHandler(initializeProtocolVersion: "2025-06-18");
        var options = new McpStreamableHttpClientOptions
        {
            Endpoint = new Uri("https://resource.example/mcp"),
            HttpMessageHandler = handler,
            OpenServerEventStream = false
        };

        var sessionResult = await StreamableHttpMcpClientSession.StartAsync(options, cancellationToken: cancellationToken);
        Assert.True(sessionResult.IsSucc);

        var session = sessionResult.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var initializeResult = await session.InitializeAsync(cancellationToken);
        Assert.True(initializeResult.IsFail);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("initialize", handler.FindSingleRequest("initialize").GetHeader("Mcp-Method"));
        Assert.Null(handler.FindSingleRequest("initialize").GetHeader("MCP-Protocol-Version"));

        await session.DisposeAsync();
    }

    [Fact]
    public async Task Session_404_Triggers_A_New_Initialize_And_New_Headers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new RecordingHttpMessageHandler(failFirstToolsListWithNotFound: true);
        var options = new McpStreamableHttpClientOptions
        {
            Endpoint = new Uri("https://resource.example/mcp"),
            HttpMessageHandler = handler,
            OpenServerEventStream = false
        };

        var sessionResult = await StreamableHttpMcpClientSession.StartAsync(options, cancellationToken: cancellationToken);
        Assert.True(sessionResult.IsSucc);

        var session = sessionResult.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var initializeResult = await session.InitializeAsync(cancellationToken);
        Assert.True(initializeResult.IsSucc);

        var firstToolsResult = await session.ListToolsAsync(null, cancellationToken);
        Assert.True(firstToolsResult.IsFail);

        var secondToolsResult = await session.ListToolsAsync(null, cancellationToken);
        Assert.True(secondToolsResult.IsSucc);

        using var argumentsDocument = JsonDocument.Parse("""{"region":"Hello, 世界","query":"SELECT * FROM users"}""");
        var callResult = await session.CallToolAsync("execute_sql", argumentsDocument.RootElement.Clone(), cancellationToken);
        Assert.True(callResult.IsSucc);

        await session.DisposeAsync();

        var initializeRequests = handler.FindRequests("initialize");
        Assert.Equal(2, initializeRequests.Length);
        Assert.All(initializeRequests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.HttpMethod);
            Assert.Null(request.GetHeader("MCP-Session-Id"));
            Assert.Null(request.GetHeader("MCP-Protocol-Version"));
        });

        var initializedNotifications = handler.FindRequests(McpMethods.NotificationsInitialized);
        Assert.Equal(2, initializedNotifications.Length);
        Assert.Equal("session-123", initializedNotifications[0].GetHeader("MCP-Session-Id"));
        Assert.Equal("session-456", initializedNotifications[1].GetHeader("MCP-Session-Id"));

        var toolsListRequests = handler.FindRequests(McpMethods.ToolsList);
        Assert.Equal(2, toolsListRequests.Length);
        Assert.Equal("session-123", toolsListRequests[0].GetHeader("MCP-Session-Id"));
        Assert.Equal("session-456", toolsListRequests[1].GetHeader("MCP-Session-Id"));

        var toolsCallRequest = handler.FindSingleRequest(McpMethods.ToolsCall);
        Assert.Equal("session-456", toolsCallRequest.GetHeader("MCP-Session-Id"));

        var deleteRequest = handler.FindSingleRequest(HttpMethod.Delete);
        Assert.Equal("session-456", deleteRequest.GetHeader("MCP-Session-Id"));
    }

    [Fact]
    public async Task Authorization_Challenge_Passes_Discovered_Auth_Server_Metadata_To_Provider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new AuthorizationDiscoveryHttpMessageHandler();
        var provider = new RecordingAuthorizationProvider("access-token-123");
        var options = new McpStreamableHttpClientOptions
        {
            Endpoint = new Uri("https://resource.example/mcp"),
            HttpMessageHandler = handler,
            AuthorizationProvider = provider,
            OpenServerEventStream = false
        };

        var sessionResult = await StreamableHttpMcpClientSession.StartAsync(options, cancellationToken: cancellationToken);
        Assert.True(sessionResult.IsSucc);

        var session = sessionResult.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var initializeResult = await session.InitializeAsync(cancellationToken);
        Assert.True(initializeResult.IsSucc);

        var toolsResult = await session.ListToolsAsync(null, cancellationToken);
        Assert.True(toolsResult.IsSucc);
        Assert.NotNull(provider.LastContext);
        Assert.Equal(new Uri("https://resource.example/mcp"), provider.LastContext!.Endpoint);
        Assert.Single(provider.LastContext.AuthorizationServers);
        Assert.Equal(McpAuthorizationServerDiscoverySource.OAuthAuthorizationServerMetadata, provider.LastContext.AuthorizationServers[0].DiscoverySource);
        Assert.Equal(new Uri("https://auth.example/tenant"), provider.LastContext.AuthorizationServers[0].Issuer);
        Assert.True(provider.LastContext.AuthorizationServers[0].SupportsPkce);
        Assert.Equal(new[] { "files:read" }, provider.LastContext.RequiredScopes);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task Open_Server_Event_Stream_Responds_To_Sampling_With_EventStream_Accept_Header()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new SamplingRequestHttpMessageHandler();
        var options = new McpStreamableHttpClientOptions
        {
            Endpoint = new Uri("https://resource.example/mcp"),
            HttpMessageHandler = handler,
            OpenServerEventStream = true,
            SupportsSampling = true,
            SamplingRequestHandler = new TestSamplingHandler()
        };

        var sessionResult = await StreamableHttpMcpClientSession.StartAsync(options, cancellationToken: cancellationToken);
        Assert.True(sessionResult.IsSucc);

        var session = sessionResult.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var initializeResult = await session.InitializeAsync(cancellationToken);
        Assert.True(initializeResult.IsSucc);

        var samplingResponseRequest = await handler.WaitForSamplingResponseRequestAsync().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.Equal(HttpMethod.Post, samplingResponseRequest.HttpMethod);
        Assert.Equal(McpMethods.SamplingCreateMessage, samplingResponseRequest.GetHeader("Mcp-Method"));
        Assert.Equal("session-123", samplingResponseRequest.GetHeader("MCP-Session-Id"));
        Assert.Contains("application/json", samplingResponseRequest.AcceptValues);
        Assert.Contains("text/event-stream", samplingResponseRequest.AcceptValues);

        await session.DisposeAsync();
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _initializeProtocolVersion;
        private readonly bool _failFirstToolsListWithNotFound;
        private readonly ConcurrentQueue<CapturedRequest> _requests = new ConcurrentQueue<CapturedRequest>();
        private readonly TaskCompletionSource<CapturedRequest> _eventStreamRequest = new TaskCompletionSource<CapturedRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _initializeCallCount;
        private int _toolsListCallCount;
        private string? _currentSessionId;

        public RecordingHttpMessageHandler(string initializeProtocolVersion = McpProtocolVersions.Current, bool failFirstToolsListWithNotFound = false)
        {
            _initializeProtocolVersion = initializeProtocolVersion;
            _failFirstToolsListWithNotFound = failFirstToolsListWithNotFound;
        }

        public int RequestCount => _requests.Count;

        public Task<CapturedRequest> GetEventStreamRequestAsync()
        {
            return _eventStreamRequest.Task;
        }

        public CapturedRequest FindSingleRequest(string mcpMethod)
        {
            var matches = _requests.Where(request => string.Equals(request.GetHeader("Mcp-Method"), mcpMethod, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException($"Expected exactly one request for '{mcpMethod}', but found {matches.Length}.");
            }

            return matches[0];
        }

        public CapturedRequest FindSingleRequest(HttpMethod httpMethod)
        {
            var matches = _requests.Where(request => request.HttpMethod == httpMethod).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException($"Expected exactly one HTTP request for '{httpMethod}', but found {matches.Length}.");
            }

            return matches[0];
        }

        public CapturedRequest[] FindRequests(string mcpMethod)
        {
            return _requests.Where(request => string.Equals(request.GetHeader("Mcp-Method"), mcpMethod, StringComparison.Ordinal)).ToArray();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = await CaptureRequestAsync(request, cancellationToken).ConfigureAwait(false);
            _requests.Enqueue(captured);

            if (request.Method == HttpMethod.Get)
            {
                _eventStreamRequest.TrySetResult(captured);
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
                {
                    Headers =
                    {
                        { "Allow", "POST" }
                    }
                };
            }

            if (request.Method == HttpMethod.Delete)
            {
                Assert.Equal(_currentSessionId, captured.GetHeader("MCP-Session-Id"));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            var method = captured.GetHeader("Mcp-Method");
            if (string.IsNullOrWhiteSpace(method))
            {
                throw new InvalidOperationException("Mcp-Method header was missing.");
            }

            if (string.Equals(method, McpMethods.Initialize, StringComparison.Ordinal))
            {
                Assert.Equal(McpMethods.Initialize, method);
                Assert.Null(captured.GetHeader("MCP-Protocol-Version"));
                _initializeCallCount++;
                _currentSessionId = _initializeCallCount == 1 ? "session-123" : "session-456";
                var initializeResultJson = "{\"protocolVersion\":\"" + _initializeProtocolVersion + "\",\"capabilities\":{\"tools\":{},\"logging\":{},\"resources\":{\"subscribe\":true},\"prompts\":{},\"completions\":{}},\"serverInfo\":{\"name\":\"test\",\"version\":\"1.0.0\"}}";
                return CreateJsonRpcResponse(captured, initializeResultJson, sessionId: _currentSessionId);
            }

            Assert.Equal(McpProtocolVersions.Current, captured.GetHeader("MCP-Protocol-Version"));
            Assert.Equal(_currentSessionId, captured.GetHeader("MCP-Session-Id"));
            Assert.Contains("application/json", captured.AcceptValues);

            if (string.Equals(method, McpMethods.NotificationsInitialized, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            if (string.Equals(method, McpMethods.ToolsList, StringComparison.Ordinal))
            {
                if (_failFirstToolsListWithNotFound && _toolsListCallCount == 0)
                {
                    _toolsListCallCount++;
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                _toolsListCallCount++;
                return CreateJsonRpcResponse(captured, """
                    {"tools":[{"name":"execute_sql","inputSchema":{"type":"object","properties":{"region":{"type":"string","x-mcp-header":"Region"},"query":{"type":"string"}},"required":["region","query"]}}],"nextCursor":null}
                    """);
            }

            if (string.Equals(method, McpMethods.ToolsCall, StringComparison.Ordinal))
            {
                return CreateJsonRpcResponse(captured, """
                    {"content":[{"type":"text","text":"ok"}],"isError":false}
                    """);
            }

            throw new InvalidOperationException($"Unexpected MCP method '{method}'.");
        }

        private static HttpResponseMessage CreateJsonRpcResponse(CapturedRequest captured, string resultJson, string? sessionId = null)
        {
            using var document = JsonDocument.Parse(captured.Body);
            var id = document.RootElement.GetProperty("id");
            var responseJson = "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":" + resultJson + "}";

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                response.Headers.TryAddWithoutValidation("MCP-Session-Id", sessionId);
            }

            return response;
        }

        private static async Task<CapturedRequest> CaptureRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }

            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers[header.Key] = header.Value.ToArray();
                }
            }

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new CapturedRequest(request.Method, request.RequestUri ?? throw new InvalidOperationException("Request URI was missing."), headers, body);
        }
    }

    private sealed class SamplingRequestHttpMessageHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<CapturedRequest> _samplingResponseRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _sessionId = "session-123";

        public Task<CapturedRequest> WaitForSamplingResponseRequestAsync()
        {
            return _samplingResponseRequest.Task;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = await CaptureRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (request.Method == HttpMethod.Post && string.Equals(captured.GetHeader("Mcp-Method"), McpMethods.Initialize, StringComparison.Ordinal))
            {
                return CreateJsonRpcResponse(captured, """
                    {"protocolVersion":"2025-11-25","capabilities":{"tools":{},"sampling":{}},"serverInfo":{"name":"test","version":"1.0.0"}}
                    """, sessionId: _sessionId);
            }

            if (request.Method == HttpMethod.Post && string.Equals(captured.GetHeader("Mcp-Method"), McpMethods.NotificationsInitialized, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"""
                        id: {_sessionId}:0
                        retry: 3000

                        id: {_sessionId}:1
                        data: {CreateServerRequestPayload()}

                        """,
                        Encoding.UTF8,
                        "text/event-stream")
                };
            }

            if (request.Method == HttpMethod.Post && string.Equals(captured.GetHeader("Mcp-Method"), McpMethods.SamplingCreateMessage, StringComparison.Ordinal))
            {
                Assert.Equal(_sessionId, captured.GetHeader("MCP-Session-Id"));
                Assert.Equal(McpProtocolVersions.Current, captured.GetHeader("MCP-Protocol-Version"));
                _samplingResponseRequest.TrySetResult(captured);
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            if (request.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        }

        private static string CreateServerRequestPayload()
        {
            return """{"jsonrpc":"2.0","id":1,"method":"sampling/createMessage","params":{"messages":[{"role":"user","content":"Say hello in one sentence."}],"maxTokens":32}}""";
        }

        private static HttpResponseMessage CreateJsonRpcResponse(CapturedRequest captured, string resultJson, string? sessionId = null)
        {
            using var document = JsonDocument.Parse(captured.Body);
            var id = document.RootElement.GetProperty("id");
            var responseJson = "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":" + resultJson + "}";

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                response.Headers.TryAddWithoutValidation("MCP-Session-Id", sessionId);
            }

            return response;
        }

        private static async Task<CapturedRequest> CaptureRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }

            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers[header.Key] = header.Value.ToArray();
                }
            }

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new CapturedRequest(request.Method, request.RequestUri ?? throw new InvalidOperationException("Request URI was missing."), headers, body);
        }
    }

    private sealed class TestSamplingHandler : IMcpClientSamplingHandler
    {
        public ValueTask<Fin<McpClientSamplingResponse>> HandleAsync(CreateMessageRequestParams parameters, IMcpClientTaskRegistry taskRegistry, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(parameters);
            ArgumentNullException.ThrowIfNull(taskRegistry);
            cancellationToken.ThrowIfCancellationRequested();

            using var document = JsonDocument.Parse("""
            {
              "type": "text",
              "text": "Hello from the client."
            }
            """);

            var result = new CreateMessageResult
            {
                Model = "test-model",
                Role = McpRoles.Assistant,
                StopReason = "endTurn",
                Content = document.RootElement.Clone()
            };

            return ValueTask.FromResult(Fin.Succ(McpClientSamplingResponse.FromResult(result)));
        }
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public RecordingHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public int CreateClientCallCount { get; private set; }

        public string? LastClientName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreateClientCallCount++;
            LastClientName = name;
            return new HttpClient(_handler, disposeHandler: false)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }
    }

    private sealed class RecordingAuthorizationProvider : IMcpAuthorizationProvider
    {
        private readonly string _accessToken;

        public RecordingAuthorizationProvider(string accessToken)
        {
            _accessToken = accessToken;
        }

        public McpAuthorizationContext? LastContext { get; private set; }

        public ValueTask<Fin<string>> GetAccessTokenAsync(McpAuthorizationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastContext = context;
            return ValueTask.FromResult(Fin.Succ(_accessToken));
        }
    }

    private sealed class AuthorizationDiscoveryHttpMessageHandler : HttpMessageHandler
    {
        private string? _sessionId;
        private int _toolsListCallCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = await CaptureRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (request.Method == HttpMethod.Post && string.Equals(captured.GetHeader("Mcp-Method"), McpMethods.Initialize, StringComparison.Ordinal))
            {
                _sessionId = "session-123";
                return CreateJsonRpcResponse(captured, """
                    {"protocolVersion":"2025-11-25","capabilities":{"tools":{}},"serverInfo":{"name":"test","version":"1.0.0"}}
                    """, sessionId: _sessionId);
            }

            if (request.Method == HttpMethod.Post && string.Equals(captured.GetHeader("Mcp-Method"), McpMethods.ToolsList, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(captured.GetHeader("Authorization")))
                {
                    _toolsListCallCount++;
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Headers =
                        {
                            { "WWW-Authenticate", "Bearer resource_metadata=\"https://resource.example/.well-known/oauth-protected-resource\", scope=\"files:read\"" }
                        }
                    };
                }

                Assert.Equal("Bearer access-token-123", captured.GetHeader("Authorization"));
                Assert.Equal(_sessionId, captured.GetHeader("MCP-Session-Id"));
                Assert.Equal(McpProtocolVersions.Current, captured.GetHeader("MCP-Protocol-Version"));

                return CreateJsonRpcResponse(captured, """
                    {"tools":[{"name":"execute_sql","inputSchema":{"type":"object","properties":{"region":{"type":"string","x-mcp-header":"Region"},"query":{"type":"string"}},"required":["region","query"]}}],"nextCursor":null}
                    """);
            }

            if (request.Method == HttpMethod.Post && string.Equals(captured.GetHeader("Mcp-Method"), McpMethods.NotificationsInitialized, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri == "https://resource.example/.well-known/oauth-protected-resource")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"resource":"https://resource.example/mcp","authorization_servers":["https://auth.example/tenant"],"scopes_supported":["files:read"]}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri == "https://auth.example/.well-known/oauth-authorization-server/tenant")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"issuer":"https://auth.example/tenant","authorization_endpoint":"https://auth.example/authorize","token_endpoint":"https://auth.example/token","client_id_metadata_document_supported":true,"code_challenge_methods_supported":["S256"]}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (request.Method == HttpMethod.Delete)
            {
                Assert.Equal(_sessionId, captured.GetHeader("MCP-Session-Id"));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        }

        private static HttpResponseMessage CreateJsonRpcResponse(CapturedRequest captured, string resultJson, string? sessionId = null)
        {
            using var document = JsonDocument.Parse(captured.Body);
            var id = document.RootElement.GetProperty("id");
            var responseJson = "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":" + resultJson + "}";

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                response.Headers.TryAddWithoutValidation("MCP-Session-Id", sessionId);
            }

            return response;
        }

        private static async Task<CapturedRequest> CaptureRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }

            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers[header.Key] = header.Value.ToArray();
                }
            }

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new CapturedRequest(request.Method, request.RequestUri ?? throw new InvalidOperationException("Request URI was missing."), headers, body);
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The fallback handler should not be used when an IHttpClientFactory is provided.");
        }
    }

    private sealed record CapturedRequest(HttpMethod HttpMethod, Uri RequestUri, IReadOnlyDictionary<string, string[]> Headers, string Body)
    {
        public string[] AcceptValues => GetHeaderValues("Accept");

        public string? GetHeader(string name)
        {
            return Headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
        }

        public string[] GetHeaderValues(string name)
        {
            return Headers.TryGetValue(name, out var values) ? values : Array.Empty<string>();
        }
    }
}
