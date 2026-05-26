using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Autofac;
using LanguageExt;
using MCPServer.Application;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure;
using MCPServer.Infrastructure.Mcp.Http;
using MCPServer.Infrastructure.Mcp.Stdio;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPServer.UnitTests.Mcp.Http;

public sealed class StreamableHttpMcpServerServiceTests
{
    [Fact]
    public async Task Live_Http_Stream_Supports_Server_Initiated_Request_RoundTrip_And_Delete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var port = await GetAvailablePortAsync(cancellationToken);

        using var container = BuildContainer(port);
        var hostedService = ResolveHttpHostedService(container);
        var sessionTransport = container.Resolve<IStreamableHttpMcpSessionTransport>();

        await hostedService.StartAsync(cancellationToken);
        await WaitForPortAsync(port, cancellationToken);

        try
        {
            using var client = CreateHttpClient(port);

            using var initializeResponse = await SendJsonAsync(
                client,
                HttpMethod.Post,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"sampling":{}},"clientInfo":{"name":"test","version":"1"}}}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: false,
                methodHeader: McpMethods.Initialize,
                cancellationToken: cancellationToken);

            Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);
            Assert.Equal("application/json", initializeResponse.Content.Headers.ContentType?.MediaType);

            using var initializeDocument = JsonDocument.Parse(await initializeResponse.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal(McpProtocolVersions.Current, initializeDocument.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());

            var sessionId = GetRequiredHeader(initializeResponse, StreamableHttpMcpHeaderNames.SessionId);

            using (var initializedResponse = await SendJsonAsync(
                client,
                HttpMethod.Post,
                """
                {"jsonrpc":"2.0","method":"notifications/initialized"}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: true,
                protocolVersion: McpProtocolVersions.Current,
                methodHeader: McpMethods.NotificationsInitialized,
                sessionId: sessionId,
                cancellationToken: cancellationToken))
            {
                Assert.Equal(HttpStatusCode.Accepted, initializedResponse.StatusCode);
            }

            using var getRequest = CreateRequest(
                HttpMethod.Get,
                origin: "http://127.0.0.1",
                includeProtocolVersion: true,
                protocolVersion: McpProtocolVersions.Current,
                sessionId: sessionId);

            using var streamResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
            Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(sessionId, GetRequiredHeader(streamResponse, StreamableHttpMcpHeaderNames.SessionId));
            Assert.Equal("no-cache", GetRequiredHeader(streamResponse, "Cache-Control"));

            await using var stream = await streamResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

            var primeEvent = await ReadNextSseEventAsync(reader, cancellationToken);
            Assert.Equal($"{sessionId}:0", primeEvent.Id);
            Assert.Equal(3000, primeEvent.RetryMilliseconds);
            Assert.Equal(string.Empty, primeEvent.Data);

            var createMessageTask = sessionTransport.CreateMessageAsync(
                new CreateMessageRequestParams
                {
                    Messages =
                    [
                        new SamplingMessage
                        {
                            Role = "user",
                            Content = JsonDocument.Parse("\"hello\"").RootElement.Clone()
                        }
                    ],
                    MaxTokens = 32
                },
                cancellationToken).AsTask();

            var outboundRequestEvent = await ReadNextSseEventAsync(reader, cancellationToken);
            Assert.Equal($"{sessionId}:1", outboundRequestEvent.Id);
            Assert.Contains("sampling/createMessage", outboundRequestEvent.Data, StringComparison.Ordinal);

            using (var outboundDocument = JsonDocument.Parse(outboundRequestEvent.Data))
            {
                var outboundId = outboundDocument.RootElement.GetProperty("id");

                using var outboundResponse = await SendJsonAsync(
                    client,
                    HttpMethod.Post,
                    "{\"jsonrpc\":\"2.0\",\"id\":" + outboundId.GetRawText() + ",\"result\":{\"accepted\":true}}",
                    origin: "http://127.0.0.1",
                    includeProtocolVersion: true,
                    protocolVersion: McpProtocolVersions.Current,
                    sessionId: sessionId,
                    cancellationToken: cancellationToken);

                Assert.Equal(HttpStatusCode.Accepted, outboundResponse.StatusCode);
            }

            var outboundResult = await createMessageTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            Assert.True(outboundResult.IsSucc);

            using var deleteResponse = await SendRequestAsync(
                client,
                CreateRequest(
                    HttpMethod.Delete,
                    origin: "http://127.0.0.1",
                    includeProtocolVersion: true,
                    protocolVersion: McpProtocolVersions.Current,
                    sessionId: sessionId),
                cancellationToken: cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            using var postDeleteResponse = await SendJsonAsync(
                client,
                HttpMethod.Post,
                """
                {"jsonrpc":"2.0","method":"notifications/initialized"}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: true,
                protocolVersion: McpProtocolVersions.Current,
                methodHeader: McpMethods.NotificationsInitialized,
                sessionId: sessionId,
                cancellationToken: cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, postDeleteResponse.StatusCode);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Live_Http_Stream_Replays_From_Last_Event_Id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var port = await GetAvailablePortAsync(cancellationToken);

        using var container = BuildContainer(port);
        var hostedService = ResolveHttpHostedService(container);
        var sessionTransport = container.Resolve<IStreamableHttpMcpSessionTransport>();

        await hostedService.StartAsync(cancellationToken);
        await WaitForPortAsync(port, cancellationToken);

        try
        {
            using var client = CreateHttpClient(port);

            using var initializeResponse = await SendJsonAsync(
                client,
                HttpMethod.Post,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"tasks":{}},"clientInfo":{"name":"test","version":"1"}}}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: false,
                methodHeader: McpMethods.Initialize,
                cancellationToken: cancellationToken);

            Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);

            var sessionId = GetRequiredHeader(initializeResponse, StreamableHttpMcpHeaderNames.SessionId);

            using (var initializedResponse = await SendJsonAsync(
                client,
                HttpMethod.Post,
                """
                {"jsonrpc":"2.0","method":"notifications/initialized"}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: true,
                protocolVersion: McpProtocolVersions.Current,
                methodHeader: McpMethods.NotificationsInitialized,
                sessionId: sessionId,
                cancellationToken: cancellationToken))
            {
                Assert.Equal(HttpStatusCode.Accepted, initializedResponse.StatusCode);
            }

            string replayCursor;

            using (var streamResponse = await client.SendAsync(
                CreateRequest(
                    HttpMethod.Get,
                    origin: "http://127.0.0.1",
                    includeProtocolVersion: true,
                    protocolVersion: McpProtocolVersions.Current,
                    sessionId: sessionId),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);

                await using var stream = await streamResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

                var primeEvent = await ReadNextSseEventAsync(reader, cancellationToken);
                Assert.Equal($"{sessionId}:0", primeEvent.Id);

                sessionTransport.Publish(new TaskStatusNotificationParams
                {
                    TaskId = "task-1",
                    Status = McpTaskStatuses.Working,
                    StatusMessage = "Working",
                    CreatedAt = "2026-05-25T10:00:00Z",
                    LastUpdatedAt = "2026-05-25T10:00:00Z"
                });

                var firstEvent = await ReadNextSseEventAsync(reader, cancellationToken);
                Assert.Equal($"{sessionId}:1", firstEvent.Id);
                Assert.Contains("task-1", firstEvent.Data, StringComparison.Ordinal);

                sessionTransport.Publish(new TaskStatusNotificationParams
                {
                    TaskId = "task-2",
                    Status = McpTaskStatuses.Working,
                    StatusMessage = "Working",
                    CreatedAt = "2026-05-25T10:00:01Z",
                    LastUpdatedAt = "2026-05-25T10:00:01Z"
                });

                var secondEvent = await ReadNextSseEventAsync(reader, cancellationToken);
                Assert.Equal($"{sessionId}:2", secondEvent.Id);
                Assert.Contains("task-2", secondEvent.Data, StringComparison.Ordinal);

                replayCursor = secondEvent.Id;
            }

            sessionTransport.Publish(new TaskStatusNotificationParams
            {
                TaskId = "task-3",
                Status = McpTaskStatuses.Working,
                StatusMessage = "Working",
                CreatedAt = "2026-05-25T10:00:02Z",
                LastUpdatedAt = "2026-05-25T10:00:02Z"
            });

            using var reconnectResponse = await WaitForReplayStreamAsync(client, sessionId, replayCursor, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, reconnectResponse.StatusCode);

            await using var reconnectStream = await reconnectResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var reconnectReader = new StreamReader(reconnectStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

            var replayedEvent = await ReadNextSseEventAsync(reconnectReader, cancellationToken);
            Assert.Equal($"{sessionId}:3", replayedEvent.Id);
            Assert.Contains("task-3", replayedEvent.Data, StringComparison.Ordinal);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    private static IContainer BuildContainer(int port)
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule(new ApplicationModule());
        builder.RegisterModule(new InfrastructureModule());

        builder.RegisterInstance(new StreamableHttpMcpTransportOptions
        {
            Enabled = true,
            Port = port,
            Path = "/mcp/",
            BindLoopbackOnly = true,
            SseRetryMilliseconds = 3000,
            SseHeartbeatMilliseconds = 100
        })
            .AsSelf()
            .SingleInstance();

        builder.Register(context => context.Resolve<IStreamableHttpMcpSessionTransport>())
            .As<IMcpClientFeatureInvoker>()
            .As<IMcpTaskStatusNotifier>()
            .SingleInstance();

        builder.RegisterInstance(new StdioMcpTransportOptions
        {
            Enabled = false
        })
            .AsSelf()
            .SingleInstance();

        return builder.Build();
    }

    private static StreamableHttpMcpServerService ResolveHttpHostedService(IContainer container)
    {
        return container.Resolve<IEnumerable<IHostedService>>()
            .OfType<StreamableHttpMcpServerService>()
            .Single();
    }

    private static HttpClient CreateHttpClient(int port)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = 10
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/mcp/"),
            Timeout = Timeout.InfiniteTimeSpan
        };

        return client;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string origin,
        bool includeProtocolVersion,
        string? protocolVersion = null,
        string? methodHeader = null,
        string? nameHeader = null,
        string? sessionId = null,
        HttpContent? content = null,
        IReadOnlyCollection<KeyValuePair<string, string>>? additionalHeaders = null)
    {
        var request = new HttpRequestMessage(method, string.Empty)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = content
        };

        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add(StreamableHttpMcpHeaderNames.Origin, origin);

        if (includeProtocolVersion)
        {
            request.Headers.Add(StreamableHttpMcpHeaderNames.ProtocolVersion, protocolVersion ?? McpProtocolVersions.Current);
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            request.Headers.Add(StreamableHttpMcpHeaderNames.SessionId, sessionId);
        }

        if (!string.IsNullOrWhiteSpace(methodHeader))
        {
            request.Headers.Add(StreamableHttpMcpHeaderNames.Method, methodHeader);
        }

        if (!string.IsNullOrWhiteSpace(nameHeader))
        {
            request.Headers.Add(StreamableHttpMcpHeaderNames.Name, nameHeader);
        }

        if (additionalHeaders is not null)
        {
            foreach (var header in additionalHeaders)
            {
                request.Headers.Add(header.Key, header.Value);
            }
        }

        return request;
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string jsonBody,
        string origin,
        bool includeProtocolVersion,
        string? protocolVersion = null,
        string? methodHeader = null,
        string? nameHeader = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await SendRequestAsync(
            client,
            CreateRequest(method, origin, includeProtocolVersion, protocolVersion, methodHeader, nameHeader, sessionId, content),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> SendRequestAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private static async Task<HttpResponseMessage> WaitForReplayStreamAsync(HttpClient client, string sessionId, string lastEventId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = CreateRequest(
                HttpMethod.Get,
                origin: "http://127.0.0.1",
                includeProtocolVersion: true,
                protocolVersion: McpProtocolVersions.Current,
                sessionId: sessionId,
                additionalHeaders:
                [
                    new KeyValuePair<string, string>(StreamableHttpMcpHeaderNames.LastEventId, lastEventId)
                ]);

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response;
            }

            var statusCode = response.StatusCode;
            response.Dispose();

            if (statusCode != HttpStatusCode.Conflict || DateTimeOffset.UtcNow >= deadline)
            {
                throw new InvalidOperationException($"Expected SSE reconnect to succeed, but received {(int)statusCode} ({statusCode}).");
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private static string GetRequiredHeader(HttpResponseMessage response, string headerName)
    {
        if (response.Headers.TryGetValues(headerName, out var values))
        {
            return values.First();
        }

        if (response.Content.Headers.TryGetValues(headerName, out values))
        {
            return values.First();
        }

        throw new InvalidOperationException($"Header '{headerName}' was missing.");
    }

    private static async Task WaitForPortAsync(int port, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<int> GetAvailablePortAsync(CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return port;
    }

    private static async Task<SseEvent> ReadNextSseEventAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            string? id = null;
            int? retry = null;
            var dataLines = new List<string>();

            while (true)
            {
                var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    throw new InvalidOperationException("The server closed the SSE stream unexpectedly.");
                }

                if (line.Length == 0)
                {
                    break;
                }

                if (line.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
                {
                    id = line[3..].TrimStart();
                    continue;
                }

                if (line.StartsWith("retry:", StringComparison.OrdinalIgnoreCase))
                {
                    var retryText = line[6..].TrimStart();
                    if (int.TryParse(retryText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRetry))
                    {
                        retry = parsedRetry;
                    }

                    continue;
                }

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    dataLines.Add(line[5..].TrimStart());
                }
            }

            if (id is null && retry is null && dataLines.Count == 0)
            {
                continue;
            }

            return new SseEvent(id ?? string.Empty, string.Join('\n', dataLines), retry);
        }
    }

    private sealed record SseEvent(string Id, string Data, int? RetryMilliseconds);
}
