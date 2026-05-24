using System.Text;
using System.Text.Json;
using Autofac;
using Autofac.Features.Indexed;
using MCPServer.Application;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class McpCapabilityHonestyTests
{
    [Fact]
    public async Task Advertised_Capabilities_Have_Registered_Method_Handlers()
    {
        using var container = BuildContainer();
        var parser = container.Resolve<IJsonRpcMessageParser>();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();
        var handlers = container.Resolve<IIndex<string, IMcpMethodHandler>>();

        var initialize = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """)));

        var dispatch = TestFin.Success(await dispatcher.DispatchAsync(initialize, CancellationToken.None));
        Assert.True(dispatch.HasResponse);
        Assert.False(dispatch.Response.IsError);
        Assert.True(dispatch.Response.Result.HasValue);

        var capabilities = dispatch.Response.Result.Value.GetProperty("capabilities");

        AssertCapabilityMethods(capabilities, "tools", handlers, McpMethods.ToolsList, McpMethods.ToolsCall);
        AssertCapabilityMethods(capabilities, "logging", handlers, McpMethods.LoggingSetLevel);
        AssertCapabilityMethods(capabilities, "resources", handlers, McpMethods.ResourcesList, McpMethods.ResourcesRead, McpMethods.ResourcesTemplatesList);
        AssertCapabilityMethods(capabilities, "prompts", handlers, McpMethods.PromptsList, McpMethods.PromptsGet);
        AssertCapabilityMethods(capabilities, "completions", handlers, McpMethods.CompletionComplete);

        var resources = capabilities.GetProperty("resources");
        if (resources.TryGetProperty("subscribe", out var subscribe) && subscribe.ValueKind is JsonValueKind.True)
        {
            Assert.True(handlers.TryGetValue(McpMethods.ResourcesSubscribe, out _));
            Assert.True(handlers.TryGetValue(McpMethods.ResourcesUnsubscribe, out _));
        }
    }

    [Fact]
    public async Task Advertised_ListChanged_Flags_Are_Not_Set_Without_Notification_Emitters()
    {
        using var container = BuildContainer();
        var parser = container.Resolve<IJsonRpcMessageParser>();
        var dispatcher = container.Resolve<IMcpRequestDispatcher>();

        var initialize = TestFin.Success(parser.Parse(Encoding.UTF8.GetBytes("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """)));

        var dispatch = TestFin.Success(await dispatcher.DispatchAsync(initialize, CancellationToken.None));
        Assert.True(dispatch.Response.Result.HasValue);
        var capabilities = dispatch.Response.Result.Value.GetProperty("capabilities");

        Assert.False(capabilities.GetProperty("tools").GetProperty("listChanged").GetBoolean());
        Assert.False(capabilities.GetProperty("prompts").GetProperty("listChanged").GetBoolean());
        Assert.False(capabilities.GetProperty("resources").TryGetProperty("listChanged", out _));
    }

    private static void AssertCapabilityMethods(JsonElement capabilities, string capabilityName, IIndex<string, IMcpMethodHandler> handlers, params string[] methodNames)
    {
        Assert.True(capabilities.TryGetProperty(capabilityName, out var capability), $"Capability '{capabilityName}' was not advertised.");
        Assert.Equal(JsonValueKind.Object, capability.ValueKind);

        for (var i = 0; i < methodNames.Length; i++)
        {
            Assert.True(handlers.TryGetValue(methodNames[i], out _), $"Capability '{capabilityName}' advertised without method handler '{methodNames[i]}'.");
        }
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
