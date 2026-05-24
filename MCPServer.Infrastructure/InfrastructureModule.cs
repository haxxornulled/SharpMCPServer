using Autofac;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Infrastructure.Mcp.JsonRpc;
using MCPServer.Infrastructure.Mcp.JsonSchema;
using MCPServer.Infrastructure.Mcp.Stdio;
using Microsoft.Extensions.Hosting;

namespace MCPServer.Infrastructure;

public sealed class InfrastructureModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterType<JsonSchemaNetToolArgumentValidator>()
            .As<IMcpToolArgumentValidator>()
            .SingleInstance();

        builder.RegisterType<JsonRpcMessageParser>()
            .As<IJsonRpcMessageParser>()
            .SingleInstance();

        builder.RegisterInstance(new JsonRpcSerializationOptions())
            .SingleInstance();

        builder.RegisterType<JsonRpcResponseSerializer>()
            .As<IJsonRpcResponseSerializer>()
            .SingleInstance();

        builder.RegisterInstance(new StdioMcpTransportOptions())
            .SingleInstance();

        builder.RegisterType<StdioMcpServerService>()
            .As<IHostedService>()
            .SingleInstance();
    }
}
