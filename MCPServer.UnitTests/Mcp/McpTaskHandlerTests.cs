using System.Text;
using System.Text.Json;
using Autofac;
using LanguageExt;
using MCPServer.Application;
using MCPServer.Application.Mcp;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class McpTaskHandlerTests
{
    [Fact]
    public async Task Tasks_List_Get_Result_And_Cancel_Work_For_Seeded_Task()
    {
        using var container = BuildContainer();
        var parser = container.Resolve<IJsonRpcMessageParser>();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        _ = await InitializeAsync(parser, dispatcher);

        var list = TestFin.Success(await DispatchRequestAsync(parser, dispatcher, 10, McpMethods.TasksList, null));
        Assert.True(list.TryGetProperty("tasks", out var tasks));
        var taskArray = tasks.EnumerateArray().ToArray();
        Assert.NotEmpty(taskArray);

        var firstTaskId = taskArray[0].GetProperty("taskId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(firstTaskId));

        using var getParams = JsonDocument.Parse($$"""{"taskId":"{{firstTaskId}}"}""");
        var get = TestFin.Success(await DispatchRequestAsync(parser, dispatcher, 11, McpMethods.TasksGet, getParams.RootElement));
        Assert.Equal(firstTaskId, get.GetProperty("taskId").GetString());

        using var resultParams = JsonDocument.Parse($$"""{"taskId":"{{firstTaskId}}"}""");
        var result = TestFin.Success(await DispatchRequestAsync(parser, dispatcher, 12, McpMethods.TasksResult, resultParams.RootElement));
        Assert.True(result.TryGetProperty("content", out _));

        using var cancelParams = JsonDocument.Parse($$"""{"taskId":"{{firstTaskId}}"}""");
        var cancel = TestFin.Success(await DispatchRequestAsync(parser, dispatcher, 13, McpMethods.TasksCancel, cancelParams.RootElement));
        Assert.Equal(firstTaskId, cancel.GetProperty("taskId").GetString());
        Assert.True(cancel.TryGetProperty("status", out var status));
        Assert.True(status.GetString() is McpTaskStatuses.Completed or McpTaskStatuses.Cancelled);
    }

    [Fact]
    public async Task Tasks_Get_And_Cancel_Return_InvalidParams_For_Missing_Task()
    {
        using var container = BuildContainer();
        var parser = container.Resolve<IJsonRpcMessageParser>();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        _ = await InitializeAsync(parser, dispatcher);

        using var missingParams = JsonDocument.Parse("""{"taskId":"missing-task"}""");
        var get = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes($$"""{"jsonrpc":"2.0","id":21,"method":"tasks/get","params":{{missingParams.RootElement.GetRawText()}}}"""))), CancellationToken.None));
        Assert.True(get.HasResponse);
        Assert.True(get.Response.IsError);

        var cancel = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes($$"""{"jsonrpc":"2.0","id":22,"method":"tasks/cancel","params":{{missingParams.RootElement.GetRawText()}}}"""))), CancellationToken.None));
        Assert.True(cancel.HasResponse);
        Assert.True(cancel.Response.IsError);
    }

    private static async Task<JsonRpcDispatchResult> InitializeAsync(IJsonRpcMessageParser parser, IMcpRequestDispatcher dispatcher)
    {
        var initialize = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"tasks":{"list":{},"cancel":{},"requests":{"sampling":{"createMessage":{}},"elicitation":{"create":{}}}},"elicitation":{"form":{},"url":{}}},"clientInfo":{"name":"test","version":"1"}}}
        """)));

        var result = TestFin.Success(await dispatcher.DispatchAsync(initialize, CancellationToken.None));
        _ = TestFin.Success(await dispatcher.DispatchAsync(TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""))), CancellationToken.None));
        return result;
    }

    private static async Task<Fin<JsonElement>> DispatchRequestAsync(IJsonRpcMessageParser parser, IMcpRequestDispatcher dispatcher, int id, string method, JsonElement? parameters)
    {
        var frame = parameters is { } supplied
            ? $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{supplied.GetRawText()}}}"""
            : $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}"}""";

        var request = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes(frame)));
        var result = TestFin.Success(await dispatcher.DispatchAsync(request, CancellationToken.None));
        Assert.True(result.HasResponse);
        Assert.False(result.Response.IsError, result.Response.Error.HasValue ? result.Response.Error.Value.Message : string.Empty);
        Assert.True(result.Response.Result.HasValue);
        return Fin.Succ(result.Response.Result.Value.Clone());
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
