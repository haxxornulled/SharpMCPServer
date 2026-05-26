using System.Text;
using MCPServer.Infrastructure.Mcp.JsonRpc;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class JsonRpcMessageParserTests
{
    [Fact]
    public void Parse_Request_Preserves_Method_And_Id()
    {
        var parser = new JsonRpcMessageParser();
        var json = Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":7,"method":"ping"}
        """);

        var message = TestFin.Success(parser.Parse(json));

        Assert.Equal("2.0", message.JsonRpc);
        Assert.Equal("ping", message.Method);
        Assert.True(message.HasId);
        Assert.True(message.Id.TryGetStableKey(out var idKey));
        Assert.Equal("7", idKey);
    }


    [Fact]
    public void Parse_Request_Without_Params_Marks_Params_As_Absent()
    {
        var parser = new JsonRpcMessageParser();
        var json = Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":7,"method":"ping"}
        """);

        var message = TestFin.Success(parser.Parse(json));

        Assert.False(message.HasParams);
        Assert.False(message.Params.HasValue);
    }

    [Fact]
    public void Parse_Request_With_Object_Params_Marks_Params_As_Present()
    {
        var parser = new JsonRpcMessageParser();
        var json = Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":7,"method":"ping","params":{}}
        """);

        var message = TestFin.Success(parser.Parse(json));

        Assert.True(message.HasParams);
        Assert.True(message.Params is { ValueKind: System.Text.Json.JsonValueKind.Object });
    }

    [Fact]
    public void Parse_Batch_Message_Fails()
    {
        var parser = new JsonRpcMessageParser();
        var json = Encoding.UTF8.GetBytes("[]");

        var error = TestFin.Failure(parser.Parse(json));

        Assert.Contains("batch", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_Object_Id_Fails()
    {
        var parser = new JsonRpcMessageParser();
        var json = Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":{},"method":"ping"}
        """);

        var error = TestFin.Failure(parser.Parse(json));

        Assert.Contains("id", error.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Parse_Null_Id_Fails_Because_Mcp_Requires_String_Or_Integer_Id()
    {
        var parser = new JsonRpcMessageParser();
        var json = Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":null,"method":"ping"}
        """);

        var error = TestFin.Failure(parser.Parse(json));

        Assert.Contains("id", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("integer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_Fractional_Number_Id_Fails_Because_Mcp_Number_Ids_Are_Integers()
    {
        var parser = new JsonRpcMessageParser();
        var json = Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1.25,"method":"ping"}
        """);

        var error = TestFin.Failure(parser.Parse(json));

        Assert.Contains("id", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("integer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_Response_Message_Is_Recognized_As_Response()
    {
        var parser = new JsonRpcMessageParser();
        var json = Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":7,"result":{}}
        """);

        var message = TestFin.Success(parser.Parse(json));

        Assert.True(message.IsResponse);
        Assert.False(message.IsRequest);
        Assert.False(message.IsNotification);
    }

    [Fact]
    public void Parse_Message_With_Method_And_Result_Fails()
    {
        var parser = new JsonRpcMessageParser();
        var json = Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":7,"method":"ping","result":{}}
        """);

        var error = TestFin.Failure(parser.Parse(json));

        Assert.Contains("method", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_Response_With_Result_And_Error_Fails()
    {
        var parser = new JsonRpcMessageParser();
        var json = Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":7,"result":{},"error":{"code":-32603,"message":"bad"}}
        """);

        var error = TestFin.Failure(parser.Parse(json));

        Assert.Contains("result", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("error", error.Message, StringComparison.OrdinalIgnoreCase);
    }

}
