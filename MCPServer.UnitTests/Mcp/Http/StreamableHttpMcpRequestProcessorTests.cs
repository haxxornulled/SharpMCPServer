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

public sealed class StreamableHttpMcpRequestProcessorTests
{
    [Fact]
    public async Task ProcessAsync_Get_Request_Returns_MethodNotAllowed()
    {
        using var container = BuildContainer();
        var processor = container.Resolve<IStreamableHttpMcpRequestProcessor>();

        var response = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Get.Method,
                """
                {"jsonrpc":"2.0","id":1,"method":"ping"}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: false),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.True(response.Headers.TryGetValue("Allow", out var allow));
        Assert.Equal("POST", allow);
        Assert.Null(response.Body);
    }

    [Fact]
    public async Task ProcessAsync_Rejects_Invalid_Origin_Before_Body_Validation()
    {
        using var container = BuildContainer();
        var processor = container.Resolve<IStreamableHttpMcpRequestProcessor>();

        var response = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Post.Method,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
                """,
                origin: "https://evil.example",
                includeProtocolVersion: false),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(response.Body);

        using var document = JsonDocument.Parse(response.Body!);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ProcessAsync_Rejects_Unsupported_Protocol_Version()
    {
        using var container = BuildContainer();
        var processor = container.Resolve<IStreamableHttpMcpRequestProcessor>();

        var response = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Post.Method,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: true,
                protocolVersion: "2025-06-18"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(response.Body);

        using var document = JsonDocument.Parse(response.Body!);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, error.GetProperty("code").GetInt32());
        Assert.Contains("unsupported", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_Initialize_And_ToolsCall_Validate_Http_Headers()
    {
        using var container = BuildContainer();
        var processor = container.Resolve<IStreamableHttpMcpRequestProcessor>();

        var initializeResponse = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Post.Method,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: false,
                methodHeader: McpMethods.Initialize),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, initializeResponse.StatusCode);
        Assert.Equal("application/json", initializeResponse.ContentType);
        Assert.NotNull(initializeResponse.Body);

        using (var initializeDocument = JsonDocument.Parse(initializeResponse.Body!))
        {
            Assert.Equal("2.0", initializeDocument.RootElement.GetProperty("jsonrpc").GetString());
            Assert.Equal(1, initializeDocument.RootElement.GetProperty("id").GetInt32());
            Assert.Equal(McpProtocolVersions.Current, initializeDocument.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        }

        Assert.True(initializeResponse.Headers.TryGetValue(StreamableHttpMcpHeaderNames.SessionId, out var sessionId));
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var readyResponse = await processor.ProcessAsync(
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
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, readyResponse.StatusCode);
        Assert.Null(readyResponse.Body);

        var region = "west region";
        var regionHeader = "=?base64?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(region)) + "?=";

        var toolsCallResponse = await processor.ProcessAsync(
            CreateRequest(
                HttpMethod.Post.Method,
                """
                {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"execute_sql","arguments":{"region":"west region","query":"select 1"}}}
                """,
                origin: "http://127.0.0.1",
                includeProtocolVersion: true,
                protocolVersion: McpProtocolVersions.Current,
                methodHeader: McpMethods.ToolsCall,
                nameHeader: "execute_sql",
                sessionId: sessionId,
                additionalHeaders:
                [
                    new KeyValuePair<string, string>(StreamableHttpMcpHeaderNames.ParamPrefix + "Region", regionHeader)
                ]),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, toolsCallResponse.StatusCode);
        Assert.Equal("application/json", toolsCallResponse.ContentType);
        Assert.NotNull(toolsCallResponse.Body);

        using var toolsDocument = JsonDocument.Parse(toolsCallResponse.Body!);
        Assert.False(toolsDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule(new ApplicationModule());
        builder.RegisterModule(new InfrastructureModule());
        builder.RegisterType<HeaderBoundTool>()
            .Keyed<IMcpTool>(HeaderBoundTool.ToolName)
            .As<IMcpTool>()
            .SingleInstance();
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

    private sealed class HeaderBoundTool : IMcpTool
    {
        public const string ToolName = "execute_sql";

        private static readonly JsonElement InputSchema = CreateInputSchema();

        public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
        {
            Name = ToolName,
            Title = "Execute SQL",
            Description = "Test tool with a header-bound parameter.",
            InputSchema = InputSchema,
            Execution = new McpToolExecution
            {
                TaskSupport = McpToolTaskSupport.Forbidden
            }
        };

        public ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<Fin<ToolCallResult>>(Fin.Succ<ToolCallResult>(ToolCallResult.Text("ok")));
        }

        private static JsonElement CreateInputSchema()
        {
            using var document = JsonDocument.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "region": {
                  "type": "string",
                  "x-mcp-header": "Region"
                },
                "query": {
                  "type": "string"
                }
              },
              "required": ["region", "query"]
            }
            """);

            return document.RootElement.Clone();
        }
    }
}
