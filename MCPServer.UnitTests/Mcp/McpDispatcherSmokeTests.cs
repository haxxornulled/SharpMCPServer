using System.Text.Json;
using System.Text;
using Autofac;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp;
using MCPServer.Application;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure;
using MCPServer.Infrastructure.Mcp.JsonRpc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class McpDispatcherSmokeTests
{
    [Fact]
    public async Task Tools_List_Is_Blocked_Until_Initialized_Notification()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        var message = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"tools/list"}
        """)));

        var dispatch = TestFin.Success(await dispatcher.DispatchAsync(message, CancellationToken.None));

        Assert.True(dispatch.HasResponse);
        Assert.True(dispatch.Response.IsError);
        Assert.True(dispatch.Response.Error.HasValue);
        var error = dispatch.Response.Error.Value;
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, error.Code);
        Assert.Equal(McpErrorMessages.SessionNotInitialized, error.Message);
    }

    [Fact]
    public async Task Initialize_Then_Initialized_Then_Tools_List_Works()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        var initialize = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """)));
        var initializeResult = TestFin.Success(await dispatcher.DispatchAsync(initialize, CancellationToken.None));
        Assert.True(initializeResult.HasResponse);
        Assert.False(initializeResult.Response.IsError);

        var initialized = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """)));
        var initializedResult = TestFin.Success(await dispatcher.DispatchAsync(initialized, CancellationToken.None));
        Assert.False(initializedResult.HasResponse);

        var list = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":2,"method":"tools/list"}
        """)));
        var listResult = TestFin.Success(await dispatcher.DispatchAsync(list, CancellationToken.None));

        Assert.True(listResult.HasResponse);
        Assert.False(listResult.Response.IsError);
        Assert.True(listResult.Response.Result.HasValue);
        var listPayload = listResult.Response.Result.Value;
        var tools = listPayload.GetProperty("tools");
        Assert.Equal(JsonValueKind.Array, tools.ValueKind);
        Assert.True(tools.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Server_Info_Tool_Call_Works()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """))), CancellationToken.None));
        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """))), CancellationToken.None));

        var call = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"server.info","arguments":{}}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(call, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.False(result.Response.IsError);
        Assert.True(result.Response.Result.HasValue);
        var payload = result.Response.Result.Value;
        Assert.False(payload.GetProperty("isError").GetBoolean());
        Assert.Equal(JsonValueKind.Array, payload.GetProperty("content").ValueKind);
    }


    [Fact]
    public async Task Server_Info_Tool_Call_With_Unexpected_Argument_Returns_Tool_Error()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """))), CancellationToken.None));
        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """))), CancellationToken.None));

        var call = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"server.info","arguments":{"unexpected":true}}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(call, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.False(result.Response.IsError);
        Assert.True(result.Response.Result.HasValue);
        var payload = result.Response.Result.Value;
        Assert.True(payload.GetProperty("isError").GetBoolean());
        Assert.Contains("additional propert", payload.GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Initialize_Declares_Logging_Capability()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        var initialize = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(initialize, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.False(result.Response.IsError);
        Assert.True(result.Response.Result.HasValue);
        var capabilities = result.Response.Result.Value.GetProperty("capabilities");
        Assert.Equal(JsonValueKind.Object, capabilities.GetProperty("logging").ValueKind);
    }

    [Fact]
    public async Task Logging_SetLevel_Updates_Logging_State()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();
        var loggingState = container.Resolve<IMcpLoggingState>();

        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """))), CancellationToken.None));
        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """))), CancellationToken.None));

        var request = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":2,"method":"logging/setLevel","params":{"level":"warning"}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(request, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.False(result.Response.IsError);
        Assert.Equal(McpLogLevels.Warning, loggingState.MinimumLevel);
        Assert.True(loggingState.IsEnabled(McpLogLevels.Error));
        Assert.False(loggingState.IsEnabled(McpLogLevels.Debug));
    }

    [Fact]
    public async Task Logging_SetLevel_Rejects_Invalid_Level()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """))), CancellationToken.None));
        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """))), CancellationToken.None));

        var request = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":2,"method":"logging/setLevel","params":{"level":"verbose"}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(request, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.True(result.Response.IsError);
        Assert.True(result.Response.Error.HasValue);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, result.Response.Error.Value.Code);
    }


    [Fact]
    public async Task Request_With_Invalid_ProgressToken_Returns_Invalid_Params()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """))), CancellationToken.None));
        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """))), CancellationToken.None));

        var call = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"_meta":{"progressToken":true},"name":"server.info","arguments":{}}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(call, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.True(result.Response.IsError);
        Assert.True(result.Response.Error.HasValue);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, result.Response.Error.Value.Code);
        Assert.Contains("progressToken", result.Response.Error.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_With_Valid_ProgressToken_Is_Accepted()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """))), CancellationToken.None));
        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """))), CancellationToken.None));

        var call = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"_meta":{"progressToken":"tool-call-1"},"name":"server.info","arguments":{}}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(call, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.False(result.Response.IsError);
    }

    [Fact]
    public async Task Request_With_Invalid_Meta_Key_Returns_Invalid_Params()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """))), CancellationToken.None));
        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """))), CancellationToken.None));

        var call = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"_meta":{"bad key":true},"name":"server.info","arguments":{}}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(call, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.True(result.Response.IsError);
        Assert.True(result.Response.Error.HasValue);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, result.Response.Error.Value.Code);
        Assert.Contains("_meta", result.Response.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tools_Call_With_Non_Object_Arguments_Returns_Invalid_Params()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """))), CancellationToken.None));
        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """))), CancellationToken.None));

        var call = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"server.info","arguments":[]}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(call, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.True(result.Response.IsError);
        Assert.True(result.Response.Error.HasValue);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, result.Response.Error.Value.Code);
        Assert.Contains("arguments", result.Response.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reserved_JsonRpc_Method_Name_Is_Rejected()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        var request = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"rpc.discover"}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(request, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.True(result.Response.IsError);
        Assert.True(result.Response.Error.HasValue);
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, result.Response.Error.Value.Code);
        Assert.Contains("reserved", result.Response.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Initialize_Stores_Client_Capabilities()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();
        var sessionState = container.Resolve<IMcpSessionState>();

        var initialize = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"roots":{"listChanged":true},"sampling":{"tools":{}},"elicitation":{},"tasks":{"list":{}}},"clientInfo":{"name":"test","version":"1"}}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(initialize, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.False(result.Response.IsError);
        Assert.True(sessionState.ClientCapabilities.SupportsRoots);
        Assert.True(sessionState.ClientCapabilities.RootsListChanged);
        Assert.True(sessionState.ClientCapabilities.SupportsSampling);
        Assert.True(sessionState.ClientCapabilities.SamplingSupportsTools);
        Assert.True(sessionState.ClientCapabilities.SupportsElicitation);
        Assert.True(sessionState.ClientCapabilities.SupportsTasks);
        Assert.True(sessionState.ClientCapabilities.TasksSupportsList);
    }

    [Fact]
    public async Task Initialize_Rejects_Invalid_Client_Capability_Shape()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        var initialize = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"roots":true},"clientInfo":{"name":"test","version":"1"}}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(initialize, CancellationToken.None));

        Assert.True(result.HasResponse);
        Assert.True(result.Response.IsError);
        Assert.True(result.Response.Error.HasValue);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, result.Response.Error.Value.Code);
        Assert.Contains("roots", result.Response.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Roots_ListChanged_Notification_Updates_Session_Revision_And_Produces_No_Response()
    {
        using var container = BuildContainer();
        var parser = new JsonRpcMessageParser();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();
        var sessionState = container.Resolve<IMcpSessionState>();

        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"roots":{"listChanged":true}},"clientInfo":{"name":"test","version":"1"}}}
        """))), CancellationToken.None));
        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        """))), CancellationToken.None));

        var notification = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","method":"notifications/roots/list_changed"}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(notification, CancellationToken.None));

        Assert.False(result.HasResponse);
        Assert.Equal(1, sessionState.RootsListRevision);
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule(new ApplicationModule());
        builder.RegisterModule(new InfrastructureModule());
        return builder.Build();
    }
}
