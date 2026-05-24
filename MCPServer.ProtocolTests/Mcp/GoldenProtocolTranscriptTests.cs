using System.Globalization;
using System.Text.Json;
using MCPServer.Domain.Mcp;
using Xunit;

namespace MCPServer.ProtocolTests.Mcp;

public sealed class GoldenProtocolTranscriptTests
{
    [Fact]
    public async Task Initialize_Ready_Ping_ToolsList_And_ServerInfo_Transcript_Is_Valid()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"protocol-test","version":"1"}}}
            """,
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"ping"}
            """,
            """
            {"jsonrpc":"2.0","id":3,"method":"tools/list"}
            """,
            """
            {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"server.info","arguments":{}}}
            """);

        Assert.Equal(4, transcript.Count);
        AssertResponseId(transcript[0], 1);
        AssertResponseId(transcript[1], 2);
        AssertResponseId(transcript[2], 3);
        AssertResponseId(transcript[3], 4);

        var initializeResult = transcript[0].GetProperty("result");
        Assert.Equal("2025-11-25", initializeResult.GetProperty("protocolVersion").GetString());
        Assert.Equal(JsonValueKind.Object, initializeResult.GetProperty("capabilities").GetProperty("tools").ValueKind);
        Assert.Equal(JsonValueKind.Object, initializeResult.GetProperty("capabilities").GetProperty("logging").ValueKind);
        Assert.Equal(JsonValueKind.Object, initializeResult.GetProperty("capabilities").GetProperty("resources").ValueKind);
        Assert.Equal(JsonValueKind.Object, initializeResult.GetProperty("capabilities").GetProperty("prompts").ValueKind);
        Assert.Equal(JsonValueKind.Object, initializeResult.GetProperty("capabilities").GetProperty("completions").ValueKind);

        Assert.Equal(JsonValueKind.Object, transcript[1].GetProperty("result").ValueKind);

        var tools = transcript[2].GetProperty("result").GetProperty("tools");
        Assert.Equal(JsonValueKind.Array, tools.ValueKind);
        Assert.True(tools.GetArrayLength() >= 1);
        Assert.Contains("server.info", GetToolNames(tools));
        var serverInfoTool = FindTool(tools, "server.info");
        Assert.Equal(JsonValueKind.Object, serverInfoTool.GetProperty("outputSchema").ValueKind);
        Assert.Equal("forbidden", serverInfoTool.GetProperty("execution").GetProperty("taskSupport").GetString());

        var toolResult = transcript[3].GetProperty("result");
        Assert.False(toolResult.GetProperty("isError").GetBoolean());
        Assert.Equal(JsonValueKind.Array, toolResult.GetProperty("content").ValueKind);
        Assert.Equal("text", toolResult.GetProperty("content")[0].GetProperty("type").GetString());
    }


    [Fact]
    public async Task Prompts_List_And_Get_Transcript_Is_Valid()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"prompts/list"}
            """,
            """
            {"jsonrpc":"2.0","id":3,"method":"prompts/get","params":{"name":"server.status","arguments":{"focus":"transport compliance"}}}
            """);

        Assert.Equal(3, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertResponseId(transcript[2], 3);

        var prompts = transcript[1].GetProperty("result").GetProperty("prompts");
        Assert.Equal(JsonValueKind.Array, prompts.ValueKind);
        Assert.Contains("server.status", GetPromptNames(prompts));

        var promptResult = transcript[2].GetProperty("result");
        var messages = promptResult.GetProperty("messages");
        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("text", messages[0].GetProperty("content").GetProperty("type").GetString());
        Assert.Contains("transport compliance", messages[0].GetProperty("content").GetProperty("text").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Prompts_Get_Rejects_Non_String_Argument_Values()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"prompts/get","params":{"name":"server.status","arguments":{"focus":123}}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.InvalidParams);
    }


    [Fact]
    public async Task Completion_Complete_For_ServerStatus_Focus_Returns_Suggestions()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"completion/complete","params":{"ref":{"type":"ref/prompt","name":"server.status"},"argument":{"name":"focus","value":"tra"}}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);

        var completion = transcript[1].GetProperty("result").GetProperty("completion");
        var values = completion.GetProperty("values");
        Assert.True(values.GetArrayLength() <= 100);
        Assert.Contains("transport compliance", GetStringArray(values));
        Assert.False(completion.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Completion_Complete_Rejects_Unknown_Prompt()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"completion/complete","params":{"ref":{"type":"ref/prompt","name":"missing"},"argument":{"name":"focus","value":""}}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.InvalidParams);
    }

    [Fact]
    public async Task Completion_Complete_Rejects_Non_String_Context_Argument_Value()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"completion/complete","params":{"ref":{"type":"ref/prompt","name":"server.status"},"argument":{"name":"focus","value":"pro"},"context":{"arguments":{"focus":123}}}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.InvalidParams);
        Assert.Contains("context.arguments", transcript[1].GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Malformed_Json_Returns_Parse_Error_Frame()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync("{");

        Assert.Equal(1, transcript.Count);
        AssertError(transcript[0], JsonRpcErrorCodes.ParseError);
        Assert.False(transcript[0].TryGetProperty("id", out _));
    }


    [Fact]
    public async Task Invalid_Utf8_Frame_Returns_Parse_Error_Frame()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendRawAsync(new byte[] { (byte)'{', 0xFF, (byte)'}' });

        Assert.Equal(1, transcript.Count);
        AssertError(transcript[0], JsonRpcErrorCodes.ParseError);
        Assert.False(transcript[0].TryGetProperty("id", out _));
    }

    [Fact]
    public async Task Output_Frames_Are_Newline_Delimited_Utf8_JsonRpc_Objects_With_No_Embedded_LineBreaks()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"server.info","arguments":{}}}
            """);

        Assert.EndsWith("\n", transcript.RawUtf8, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", transcript.RawUtf8);

        var lines = transcript.RawUtf8.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(transcript.Count, lines.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            Assert.DoesNotContain("\n", lines[i]);
            Assert.DoesNotContain("\r", lines[i]);

            using var document = JsonDocument.Parse(lines[i]);
            Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
        }
    }

    [Fact]
    public async Task Invalid_Request_Id_Returns_Parse_Error_Frame()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            """
            {"jsonrpc":"2.0","id":{"bad":true},"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"protocol-test","version":"1"}}}
            """);

        Assert.Equal(1, transcript.Count);
        AssertError(transcript[0], JsonRpcErrorCodes.ParseError);
        Assert.False(transcript[0].TryGetProperty("id", out _));
    }

    [Fact]
    public async Task Unknown_Request_Method_After_Ready_Returns_Method_Not_Found()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"no/suchMethod"}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.MethodNotFound);
    }

    [Fact]
    public async Task Unknown_Notification_Method_After_Ready_Produces_No_Response()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","method":"no/suchNotification"}
            """);

        Assert.Equal(1, transcript.Count);
        AssertResponseId(transcript[0], 1);
    }

    [Fact]
    public async Task Tools_Call_Invalid_Arguments_Returns_Tool_Error_Not_Protocol_Error()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"server.info","arguments":{"unexpected":true}}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        Assert.False(transcript[1].TryGetProperty("error", out _));
        var result = transcript[1].GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("additional propert", result.GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logging_SetLevel_Transcript_Returns_Empty_Result()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"logging/setLevel","params":{"level":"warning"}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        Assert.Equal(JsonValueKind.Object, transcript[1].GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task Cancelled_Notification_Before_Initialize_Produces_No_Response()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            """
            {"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":123,"reason":"test"}}
            """);

        Assert.Equal(0, transcript.Count);
    }

    [Fact]
    public async Task Request_Before_Initialize_Returns_Session_Not_Initialized()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            """
            {"jsonrpc":"2.0","id":99,"method":"tools/list"}
            """);

        Assert.Equal(1, transcript.Count);
        AssertResponseId(transcript[0], 99);
        AssertError(transcript[0], JsonRpcErrorCodes.InvalidRequest);
        Assert.Contains("initialize", transcript[0].GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Null_Request_Id_Is_Rejected_Because_Mcp_Requires_String_Or_Integer_Ids()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            """
            {"jsonrpc":"2.0","id":null,"method":"ping"}
            """);

        Assert.Equal(1, transcript.Count);
        AssertError(transcript[0], JsonRpcErrorCodes.ParseError);
        Assert.False(transcript[0].TryGetProperty("id", out _));
    }

    [Fact]
    public async Task Fractional_Request_Id_Is_Rejected_Because_Mcp_Requires_Integer_Number_Ids()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            """
            {"jsonrpc":"2.0","id":1.25,"method":"ping"}
            """);

        Assert.Equal(1, transcript.Count);
        AssertError(transcript[0], JsonRpcErrorCodes.ParseError);
        Assert.False(transcript[0].TryGetProperty("id", out _));
    }

    [Fact]
    public async Task Params_Must_Be_Object_When_Present()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            """
            {"jsonrpc":"2.0","id":7,"method":"initialize","params":[]}
            """);

        Assert.Equal(1, transcript.Count);
        AssertResponseId(transcript[0], 7);
        AssertError(transcript[0], JsonRpcErrorCodes.InvalidRequest);
        Assert.Contains("params", transcript[0].GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Duplicate_Request_Id_In_Same_Session_Is_Rejected()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"ping"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"ping"}
            """);

        Assert.Equal(3, transcript.Count);
        AssertResponseId(transcript[2], 2);
        AssertError(transcript[2], JsonRpcErrorCodes.InvalidRequest);
        Assert.Contains("already", transcript[2].GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mcp_Notification_Method_With_Id_Is_Rejected_And_Not_Dispatched_As_Request()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","id":2,"method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":3,"method":"tools/list"}
            """);

        Assert.Equal(3, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.InvalidRequest);
        AssertResponseId(transcript[2], 3);
        AssertError(transcript[2], JsonRpcErrorCodes.InvalidRequest);
        Assert.Contains("initialized", transcript[2].GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_Notification_With_Id_Does_Not_Consume_Request_Id()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","id":2,"method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"ping"}
            """);

        Assert.Equal(3, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.InvalidRequest);
        AssertResponseId(transcript[2], 2);
        Assert.Equal(JsonValueKind.Object, transcript[2].GetProperty("result").ValueKind);
    }


    [Fact]
    public async Task JsonRpc_Response_Message_Is_Ignored_And_Never_Answered()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            """
            {"jsonrpc":"2.0","id":44,"result":{}}
            """);

        Assert.Equal(0, transcript.Count);
    }

    [Fact]
    public async Task Ping_With_RequestParams_Object_Is_Accepted()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"ping","params":{"_meta":{"progressToken":"ping-1"}}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        Assert.Equal(JsonValueKind.Object, transcript[1].GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task Initialize_Requires_ClientInfo_Name_And_Version()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"protocol-test"}}}
            """);

        Assert.Equal(1, transcript.Count);
        AssertResponseId(transcript[0], 1);
        AssertError(transcript[0], JsonRpcErrorCodes.InvalidParams);
        Assert.Contains("version", transcript[0].GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Initialize_Requires_Capabilities_Object()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","clientInfo":{"name":"protocol-test","version":"1"}}}
            """);

        Assert.Equal(1, transcript.Count);
        AssertResponseId(transcript[0], 1);
        AssertError(transcript[0], JsonRpcErrorCodes.InvalidParams);
        Assert.Contains("capabilities", transcript[0].GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tools_List_Invalid_Cursor_Returns_Invalid_Params()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{"cursor":"1"}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.InvalidParams);
        Assert.Contains("cursor", transcript[1].GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Resources_List_Read_And_TemplatesList_Transcript_Is_Valid()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"resources/list"}
            """,
            """
            {"jsonrpc":"2.0","id":3,"method":"resources/read","params":{"uri":"mcpserver://server/info"}}
            """,
            """
            {"jsonrpc":"2.0","id":4,"method":"resources/templates/list"}
            """);

        Assert.Equal(4, transcript.Count);
        AssertResponseId(transcript[0], 1);
        Assert.Equal(JsonValueKind.Object, transcript[0].GetProperty("result").GetProperty("capabilities").GetProperty("resources").ValueKind);

        AssertResponseId(transcript[1], 2);
        var resources = transcript[1].GetProperty("result").GetProperty("resources");
        Assert.Equal(JsonValueKind.Array, resources.ValueKind);
        Assert.Contains("mcpserver://server/info", GetResourceUris(resources));

        AssertResponseId(transcript[2], 3);
        var contents = transcript[2].GetProperty("result").GetProperty("contents");
        Assert.Equal(JsonValueKind.Array, contents.ValueKind);
        Assert.Equal("mcpserver://server/info", contents[0].GetProperty("uri").GetString());
        Assert.Equal("application/json", contents[0].GetProperty("mimeType").GetString());
        Assert.True(contents[0].TryGetProperty("text", out var text));
        Assert.Contains("MCPServer", text.GetString(), StringComparison.Ordinal);

        AssertResponseId(transcript[3], 4);
        Assert.Empty(transcript[3].GetProperty("result").GetProperty("resourceTemplates").EnumerateArray());
    }

    [Fact]
    public async Task Resources_Read_Missing_Uri_Returns_Invalid_Params()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"resources/read","params":{}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.InvalidParams);
        Assert.Contains("uri", transcript[1].GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Resources_Read_Unknown_Uri_Returns_Resource_Not_Found()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"resources/read","params":{"uri":"mcpserver://server/missing"}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Resources_List_Invalid_Cursor_Returns_Invalid_Params()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"resources/list","params":{"cursor":"1"}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.InvalidParams);
        Assert.Contains("cursor", transcript[1].GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Resources_Subscribe_And_Unsubscribe_Transcript_Is_Valid()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"resources/subscribe","params":{"uri":"mcpserver://server/info"}}
            """,
            """
            {"jsonrpc":"2.0","id":3,"method":"resources/unsubscribe","params":{"uri":"mcpserver://server/info"}}
            """);

        Assert.Equal(3, transcript.Count);
        AssertResponseId(transcript[0], 1);
        var resourcesCapability = transcript[0].GetProperty("result").GetProperty("capabilities").GetProperty("resources");
        Assert.True(resourcesCapability.GetProperty("subscribe").GetBoolean());

        AssertResponseId(transcript[1], 2);
        Assert.Equal(JsonValueKind.Object, transcript[1].GetProperty("result").ValueKind);

        AssertResponseId(transcript[2], 3);
        Assert.Equal(JsonValueKind.Object, transcript[2].GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task Resources_Subscribe_Unknown_Uri_Returns_Resource_Not_Found()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"resources/subscribe","params":{"uri":"mcpserver://server/missing"}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Resources_Unsubscribe_Requires_Uri()
    {
        using var harness = ProtocolTranscriptHarness.Create();

        using var transcript = await harness.SendAsync(
            InitializeFrame(1),
            """
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            """,
            """
            {"jsonrpc":"2.0","id":2,"method":"resources/unsubscribe","params":{}}
            """);

        Assert.Equal(2, transcript.Count);
        AssertResponseId(transcript[1], 2);
        AssertError(transcript[1], JsonRpcErrorCodes.InvalidParams);
        Assert.Contains("uri", transcript[1].GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string InitializeFrame(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString(CultureInfo.InvariantCulture) +
               ",\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"protocol-test\",\"version\":\"1\"}}}";
    }

    private static void AssertResponseId(JsonElement response, int expectedId)
    {
        Assert.Equal("2.0", response.GetProperty("jsonrpc").GetString());
        Assert.Equal(expectedId, response.GetProperty("id").GetInt32());
    }

    private static void AssertError(JsonElement response, int expectedErrorCode)
    {
        Assert.Equal("2.0", response.GetProperty("jsonrpc").GetString());
        var error = response.GetProperty("error");
        Assert.Equal(expectedErrorCode, error.GetProperty("code").GetInt32());
    }



    private static string[] GetStringArray(JsonElement array)
    {
        var values = new string[array.GetArrayLength()];
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            values[index++] = item.GetString() ?? string.Empty;
        }

        return values;
    }

    private static string[] GetPromptNames(JsonElement prompts)
    {
        var names = new string[prompts.GetArrayLength()];
        var index = 0;
        foreach (var prompt in prompts.EnumerateArray())
        {
            names[index++] = prompt.GetProperty("name").GetString() ?? string.Empty;
        }

        return names;
    }

    private static string[] GetToolNames(JsonElement tools)
    {
        var names = new string[tools.GetArrayLength()];
        var index = 0;
        foreach (var tool in tools.EnumerateArray())
        {
            names[index++] = tool.GetProperty("name").GetString() ?? string.Empty;
        }

        return names;
    }


    private static string[] GetResourceUris(JsonElement resources)
    {
        var uris = new string[resources.GetArrayLength()];
        var index = 0;
        foreach (var resource in resources.EnumerateArray())
        {
            uris[index++] = resource.GetProperty("uri").GetString() ?? string.Empty;
        }

        return uris;
    }

    private static JsonElement FindTool(JsonElement tools, string name)
    {
        foreach (var tool in tools.EnumerateArray())
        {
            if (string.Equals(tool.GetProperty("name").GetString(), name, StringComparison.Ordinal))
            {
                return tool;
            }
        }

        throw new InvalidOperationException($"Tool '{name}' was not found in transcript.");
    }
}
