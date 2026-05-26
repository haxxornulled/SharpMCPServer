using System.Net;
using System.Text;
using System.Text.Json;
using Autofac;
using LanguageExt;
using MCPServer.Application;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure;
using MCPServer.Infrastructure.Mcp.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPServer.UnitTests.Mcp.Http;

public sealed class StreamableHttpMcpSessionTransportTests
{
    [Fact]
    public async Task CreateMessageAsync_Writes_Sse_Events_And_Completes_When_Response_Is_Received()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var container = BuildContainer();
        var processor = container.Resolve<IStreamableHttpMcpRequestProcessor>();
        var transport = container.Resolve<IStreamableHttpMcpSessionTransport>();

        var initializeResponse = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Post.Method,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"sampling":{},"tasks":{}},"clientInfo":{"name":"test","version":"1"}}}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: false,
                methodHeader: McpMethods.Initialize),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);
        Assert.True(initializeResponse.Headers.TryGetValue(StreamableHttpMcpHeaderNames.SessionId, out var sessionId));
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var initializedResponse = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Post.Method,
                """
                {"jsonrpc":"2.0","method":"notifications/initialized"}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: true,
                protocolVersion: McpProtocolVersions.Current,
                methodHeader: McpMethods.NotificationsInitialized,
                sessionId: sessionId),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, initializedResponse.StatusCode);

        transport.Publish(new TaskStatusNotificationParams
        {
            TaskId = "task-1",
            Status = McpTaskStatuses.Working,
            StatusMessage = "Working",
            CreatedAt = "2026-05-25T10:00:00Z",
            LastUpdatedAt = "2026-05-25T10:00:00Z"
        });

        await using var stream = new RecordingStream();
        using var streamCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var openStreamTask = transport.OpenEventStreamAsync(stream, lastEventId: null, streamCancellationSource.Token).AsTask();

        await stream.WaitForWriteCountAsync(2, TimeSpan.FromSeconds(5), cancellationToken);

        var createMessageTask = transport.CreateMessageAsync(
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
                MaxTokens = 16
            },
            cancellationToken).AsTask();

        await stream.WaitForWriteCountAsync(3, TimeSpan.FromSeconds(5), cancellationToken);

        var output = stream.GetText();
        Assert.Contains($"id: {sessionId}:0", output, StringComparison.Ordinal);
        Assert.Contains("notifications/tasks/status", output, StringComparison.Ordinal);
        Assert.Contains("sampling/createMessage", output, StringComparison.Ordinal);

        using var requestResponseDocument = JsonDocument.Parse("""{"accepted":true}""");
        using var requestIdDocument = JsonDocument.Parse("1");
        Assert.True(JsonRpcRequestId.TryFromElement(requestIdDocument.RootElement, out var requestId));
        Assert.True(transport.TryHandleResponse(new JsonRpcMessage(
            "2.0",
            null,
            requestId,
            null,
            requestResponseDocument.RootElement.Clone(),
            null,
            hasResult: true,
            hasError: false)));

        var createMessageResult = await createMessageTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.True(createMessageResult.IsSucc);

        var payload = createMessageResult.Match(
            Succ: static value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.True(payload.GetProperty("accepted").GetBoolean());

        streamCancellationSource.Cancel();
        await openStreamTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    [Fact]
    public async Task OpenEventStreamAsync_Replays_Retained_History_After_Pruning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var container = BuildContainer(new StreamableHttpMcpTransportOptions
        {
            MaxSessionHistoryMessages = 2
        });
        var processor = container.Resolve<IStreamableHttpMcpRequestProcessor>();
        var transport = container.Resolve<IStreamableHttpMcpSessionTransport>();

        var initializeResponse = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Post.Method,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"sampling":{},"tasks":{}},"clientInfo":{"name":"test","version":"1"}}}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: false,
                methodHeader: McpMethods.Initialize),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);
        Assert.True(initializeResponse.Headers.TryGetValue(StreamableHttpMcpHeaderNames.SessionId, out var sessionId));
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var initializedResponse = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Post.Method,
                """
                {"jsonrpc":"2.0","method":"notifications/initialized"}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: true,
                protocolVersion: McpProtocolVersions.Current,
                methodHeader: McpMethods.NotificationsInitialized,
                sessionId: sessionId),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, initializedResponse.StatusCode);

        transport.Publish(new TaskStatusNotificationParams
        {
            TaskId = "task-1",
            Status = McpTaskStatuses.Working,
            StatusMessage = "Working 1",
            CreatedAt = "2026-05-25T10:00:00Z",
            LastUpdatedAt = "2026-05-25T10:00:00Z"
        });
        transport.Publish(new TaskStatusNotificationParams
        {
            TaskId = "task-2",
            Status = McpTaskStatuses.Working,
            StatusMessage = "Working 2",
            CreatedAt = "2026-05-25T10:00:01Z",
            LastUpdatedAt = "2026-05-25T10:00:01Z"
        });
        transport.Publish(new TaskStatusNotificationParams
        {
            TaskId = "task-3",
            Status = McpTaskStatuses.Working,
            StatusMessage = "Working 3",
            CreatedAt = "2026-05-25T10:00:02Z",
            LastUpdatedAt = "2026-05-25T10:00:02Z"
        });
        transport.Publish(new TaskStatusNotificationParams
        {
            TaskId = "task-4",
            Status = McpTaskStatuses.Working,
            StatusMessage = "Working 4",
            CreatedAt = "2026-05-25T10:00:03Z",
            LastUpdatedAt = "2026-05-25T10:00:03Z"
        });

        await using var stream = new RecordingStream();
        using var streamCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var openStreamTask = transport.OpenEventStreamAsync(stream, lastEventId: null, streamCancellationSource.Token).AsTask();

        await stream.WaitForWriteCountAsync(3, TimeSpan.FromSeconds(5), cancellationToken);

        var output = stream.GetText();
        Assert.Contains($"id: {sessionId}:0", output, StringComparison.Ordinal);
        Assert.DoesNotContain($"id: {sessionId}:1", output, StringComparison.Ordinal);
        Assert.DoesNotContain($"id: {sessionId}:2", output, StringComparison.Ordinal);
        Assert.Contains($"id: {sessionId}:3", output, StringComparison.Ordinal);
        Assert.Contains($"id: {sessionId}:4", output, StringComparison.Ordinal);

        streamCancellationSource.Cancel();
        await openStreamTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    [Fact]
    public async Task TerminateSession_Resets_Session_And_Cancels_Active_Stream()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var container = BuildContainer();
        var processor = container.Resolve<IStreamableHttpMcpRequestProcessor>();
        var transport = container.Resolve<IStreamableHttpMcpSessionTransport>();

        var initializeResponse = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Post.Method,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"sampling":{}},"clientInfo":{"name":"test","version":"1"}}}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: false,
                methodHeader: McpMethods.Initialize),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);
        Assert.True(initializeResponse.Headers.TryGetValue(StreamableHttpMcpHeaderNames.SessionId, out var sessionId));
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        await using var stream = new RecordingStream();
        using var streamCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var openStreamTask = transport.OpenEventStreamAsync(stream, lastEventId: null, streamCancellationSource.Token).AsTask();

        await stream.WaitForWriteCountAsync(1, TimeSpan.FromSeconds(5), cancellationToken);
        transport.TerminateSession();
        await openStreamTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        Assert.False(transport.HasActiveSession);
        Assert.Null(transport.SessionId);
    }

    [Fact]
    public async Task TryValidateEventStreamRequest_Rejects_Foreign_And_Too_Old_Replay_Cursors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var container = BuildContainer(new StreamableHttpMcpTransportOptions
        {
            MaxSessionHistoryMessages = 2
        });
        var processor = container.Resolve<IStreamableHttpMcpRequestProcessor>();
        var transport = container.Resolve<IStreamableHttpMcpSessionTransport>();

        var initializeResponse = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Post.Method,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"sampling":{},"tasks":{}},"clientInfo":{"name":"test","version":"1"}}}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: false,
                methodHeader: McpMethods.Initialize),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);
        Assert.True(initializeResponse.Headers.TryGetValue(StreamableHttpMcpHeaderNames.SessionId, out var sessionId));
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var initializedResponse = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Post.Method,
                """
                {"jsonrpc":"2.0","method":"notifications/initialized"}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: true,
                protocolVersion: McpProtocolVersions.Current,
                methodHeader: McpMethods.NotificationsInitialized,
                sessionId: sessionId),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, initializedResponse.StatusCode);

        transport.Publish(new TaskStatusNotificationParams
        {
            TaskId = "task-1",
            Status = McpTaskStatuses.Working,
            StatusMessage = "Working 1",
            CreatedAt = "2026-05-25T10:00:00Z",
            LastUpdatedAt = "2026-05-25T10:00:00Z"
        });
        transport.Publish(new TaskStatusNotificationParams
        {
            TaskId = "task-2",
            Status = McpTaskStatuses.Working,
            StatusMessage = "Working 2",
            CreatedAt = "2026-05-25T10:00:01Z",
            LastUpdatedAt = "2026-05-25T10:00:01Z"
        });
        transport.Publish(new TaskStatusNotificationParams
        {
            TaskId = "task-3",
            Status = McpTaskStatuses.Working,
            StatusMessage = "Working 3",
            CreatedAt = "2026-05-25T10:00:02Z",
            LastUpdatedAt = "2026-05-25T10:00:02Z"
        });
        transport.Publish(new TaskStatusNotificationParams
        {
            TaskId = "task-4",
            Status = McpTaskStatuses.Working,
            StatusMessage = "Working 4",
            CreatedAt = "2026-05-25T10:00:03Z",
            LastUpdatedAt = "2026-05-25T10:00:03Z"
        });

        var currentSessionId = sessionId;
        Assert.False(string.IsNullOrWhiteSpace(currentSessionId));

        Assert.True(transport.TryValidateEventStreamRequest($"{currentSessionId}:2", out var retainedStatusCode, out var retainedError));
        Assert.Equal(HttpStatusCode.OK, retainedStatusCode);
        Assert.True(string.IsNullOrWhiteSpace(retainedError));

        Assert.False(transport.TryValidateEventStreamRequest($"{currentSessionId}:1", out var tooOldStatusCode, out var tooOldError));
        Assert.Equal(HttpStatusCode.Conflict, tooOldStatusCode);
        Assert.Contains("replay cursor", tooOldError, StringComparison.OrdinalIgnoreCase);

        Assert.False(transport.TryValidateEventStreamRequest($"{currentSessionId}:99", out var futureStatusCode, out var futureError));
        Assert.Equal(HttpStatusCode.Conflict, futureStatusCode);
        Assert.Contains("replay cursor", futureError, StringComparison.OrdinalIgnoreCase);

        Assert.False(transport.TryValidateEventStreamRequest("other-session:0", out var foreignStatusCode, out var foreignError));
        Assert.Equal(HttpStatusCode.Conflict, foreignStatusCode);
        Assert.Contains("does not match", foreignError, StringComparison.OrdinalIgnoreCase);

        transport.TerminateSession();

        Assert.False(transport.TryValidateEventStreamRequest($"{currentSessionId}:0", out var missingStatusCode, out var missingError));
        Assert.Equal(HttpStatusCode.NotFound, missingStatusCode);
        Assert.Contains("Session not found", missingError, StringComparison.OrdinalIgnoreCase);
    }

    private static IContainer BuildContainer(StreamableHttpMcpTransportOptions? streamableHttpOptions = null)
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule(new ApplicationModule());
        builder.RegisterModule(new InfrastructureModule());
        if (streamableHttpOptions is not null)
        {
            builder.RegisterInstance(streamableHttpOptions)
                .AsSelf()
                .SingleInstance();
        }
        return builder.Build();
    }

    private static StreamableHttpMcpRequest CreateRequest(
        string method,
        string body,
        string origin,
        bool includeProtocolVersion,
        string? protocolVersion = null,
        string? methodHeader = null,
        string? nameHeader = null,
        string? sessionId = null,
        IReadOnlyCollection<KeyValuePair<string, string>>? additionalHeaders = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [StreamableHttpMcpHeaderNames.Accept] = "application/json, text/event-stream",
            [StreamableHttpMcpHeaderNames.ContentType] = "application/json",
            [StreamableHttpMcpHeaderNames.Origin] = origin
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            headers[StreamableHttpMcpHeaderNames.SessionId] = sessionId;
        }

        if (includeProtocolVersion)
        {
            headers[StreamableHttpMcpHeaderNames.ProtocolVersion] = protocolVersion ?? McpProtocolVersions.Current;
        }

        if (!string.IsNullOrWhiteSpace(methodHeader))
        {
            headers[StreamableHttpMcpHeaderNames.Method] = methodHeader;
        }

        if (!string.IsNullOrWhiteSpace(nameHeader))
        {
            headers[StreamableHttpMcpHeaderNames.Name] = nameHeader;
        }

        if (additionalHeaders is not null)
        {
            foreach (var header in additionalHeaders)
            {
                headers[header.Key] = header.Value;
            }
        }

        return new StreamableHttpMcpRequest
        {
            Method = method,
            RequestUri = new Uri("http://127.0.0.1/mcp/"),
            Headers = headers,
            Body = Encoding.UTF8.GetBytes(body)
        };
    }

    private sealed class RecordingStream : Stream
    {
        private readonly MemoryStream _buffer = new MemoryStream();
        private readonly object _gate = new object();
        private int _writeCount;

        public int WriteCount => Volatile.Read(ref _writeCount);

        public string GetText()
        {
            lock (_gate)
            {
                return Encoding.UTF8.GetString(_buffer.ToArray());
            }
        }

        public async Task WaitForWriteCountAsync(int minimumWriteCount, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var deadline = DateTimeOffset.UtcNow.Add(timeout);
            while (WriteCount < minimumWriteCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    throw new TimeoutException($"Expected at least {minimumWriteCount} stream writes, but observed {WriteCount}.");
                }

                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length
        {
            get
            {
                lock (_gate)
                {
                    return _buffer.Length;
                }
            }
        }

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_gate)
            {
                _buffer.Write(buffer, offset, count);
                Interlocked.Increment(ref _writeCount);
            }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _buffer.Write(buffer.Span);
                Interlocked.Increment(ref _writeCount);
            }

            return ValueTask.CompletedTask;
        }
    }
}
