using System.Text.Json;
using System.Text;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure.Mcp.JsonRpc;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class JsonRpcResponseSerializerTests
{
    [Fact]
    public async Task WriteAsync_Writes_Compact_Newline_Delimited_Response()
    {
        using var idDocument = JsonDocument.Parse("123");
        var requestId = JsonRpcRequestId.FromClonedElement(idDocument.RootElement.Clone());
        var response = JsonRpcResponse.Success(requestId, McpJsonElements.EmptyObject);
        var serializer = new JsonRpcResponseSerializer();
        await using var output = new MemoryStream();

        await serializer.WriteAsync(output, response, CancellationToken.None);

        var bytes = output.ToArray();
        Assert.Equal((byte)'\n', bytes[^1]);

        using var document = JsonDocument.Parse(bytes.AsMemory(0, bytes.Length - 1));
        var root = document.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(123, root.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("result").ValueKind);
    }


    [Fact]
    public async Task WriteAsync_Omits_Id_When_Request_Id_Could_Not_Be_Read()
    {
        var serializer = new JsonRpcResponseSerializer();
        await using var output = new MemoryStream();
        var response = JsonRpcResponse.Failure(JsonRpcRequestId.Missing, JsonRpcErrorCodes.ParseError, "Parse error.");

        await serializer.WriteAsync(output, response, CancellationToken.None);

        var bytes = output.ToArray();
        using var document = JsonDocument.Parse(bytes.AsMemory(0, bytes.Length - 1));
        var root = document.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.False(root.TryGetProperty("id", out _));
        Assert.Equal(JsonRpcErrorCodes.ParseError, root.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task WriteAsync_Rejects_Frames_Larger_Than_Configured_Maximum()
    {
        var serializer = new JsonRpcResponseSerializer(new JsonRpcSerializationOptions
        {
            InitialBufferBytes = 128,
            MaxOutputFrameBytes = 1_024
        });
        await using var output = new MemoryStream();
        using var idDocument = JsonDocument.Parse("1");
        using var resultDocument = JsonDocument.Parse("{\"payload\":\"" + new string('x', 2_000) + "\"}");
        var response = JsonRpcResponse.Success(
            JsonRpcRequestId.FromClonedElement(idDocument.RootElement.Clone()),
            resultDocument.RootElement.Clone());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await serializer.WriteAsync(output, response, CancellationToken.None));

        Assert.Contains("exceeded", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, output.Length);
    }


    [Fact]
    public async Task WriteRequestAsync_Writes_Request_With_Id_Method_And_Params()
    {
        var serializer = new JsonRpcResponseSerializer();
        await using var output = new MemoryStream();

        await serializer.WriteRequestAsync(output, 42, McpMethods.SamplingCreateMessage, McpJsonElements.EmptyObject, CancellationToken.None);

        var bytes = output.ToArray();
        using var document = JsonDocument.Parse(bytes.AsMemory(0, bytes.Length - 1));
        var root = document.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(42, root.GetProperty("id").GetInt32());
        Assert.Equal(McpMethods.SamplingCreateMessage, root.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("params").ValueKind);
    }

    [Fact]
    public async Task WriteNotificationAsync_Writes_Notification_Without_Id()
    {
        var serializer = new JsonRpcResponseSerializer();
        await using var output = new MemoryStream();

        await serializer.WriteNotificationAsync(output, McpMethods.NotificationsProgress, McpJsonElements.EmptyObject, CancellationToken.None);

        var bytes = output.ToArray();
        using var document = JsonDocument.Parse(bytes.AsMemory(0, bytes.Length - 1));
        var root = document.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(McpMethods.NotificationsProgress, root.GetProperty("method").GetString());
        Assert.False(root.TryGetProperty("id", out _));
        Assert.Equal(JsonValueKind.Object, root.GetProperty("params").ValueKind);
    }

    [Fact]
    public async Task WriteNotificationAsync_Escapes_LineBreaks_And_Writes_Exactly_One_Frame_Delimiter()
    {
        var serializer = new JsonRpcResponseSerializer();
        await using var output = new MemoryStream();
        using var parameters = JsonDocument.Parse("""{"text":"line1\nline2","carriage":"a\rb"}""");

        await serializer.WriteNotificationAsync(output, McpMethods.NotificationsMessage, parameters.RootElement.Clone(), CancellationToken.None);

        var raw = Encoding.UTF8.GetString(output.ToArray());
        Assert.EndsWith("\n", raw, StringComparison.Ordinal);

        var frame = raw[..^1];
        Assert.DoesNotContain("\n", frame);
        Assert.DoesNotContain("\r", frame);
        Assert.Contains("\\n", frame);
        Assert.Contains("\\r", frame);

        using var document = JsonDocument.Parse(frame);
        Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
    }

    [Fact]
    public async Task WriteNotificationAsync_Writes_Structured_Logging_Message()
    {
        var serializer = new JsonRpcResponseSerializer();
        await using var output = new MemoryStream();
        using var data = JsonDocument.Parse("""{"operation":"test"}""");
        var parameters = new LoggingMessageNotificationParams
        {
            Level = McpLogLevels.Info,
            Logger = "unit-test",
            Data = data.RootElement.Clone()
        };
        var payload = JsonSerializer.SerializeToElement(parameters, McpJsonSerializerContext.Default.LoggingMessageNotificationParams);

        await serializer.WriteNotificationAsync(output, McpMethods.NotificationsMessage, payload, CancellationToken.None);

        var bytes = output.ToArray();
        using var document = JsonDocument.Parse(bytes.AsMemory(0, bytes.Length - 1));
        var root = document.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(McpMethods.NotificationsMessage, root.GetProperty("method").GetString());
        var jsonParams = root.GetProperty("params");
        Assert.Equal(McpLogLevels.Info, jsonParams.GetProperty("level").GetString());
        Assert.Equal("unit-test", jsonParams.GetProperty("logger").GetString());
        Assert.Equal("test", jsonParams.GetProperty("data").GetProperty("operation").GetString());
    }

}
