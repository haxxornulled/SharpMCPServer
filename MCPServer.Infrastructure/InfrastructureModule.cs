using Autofac;
using MCPServer.Application.Mcp.JsonRpc.Interfaces;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp;
using MCPServer.Infrastructure.Mcp.Http.Authorization;
using MCPServer.Infrastructure.Mcp.Http;
using MCPServer.Infrastructure.Mcp.JsonRpc;
using MCPServer.Infrastructure.Mcp.JsonSchema;
using MCPServer.Infrastructure.Mcp.Stdio;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

        builder.RegisterType<StdioMcpClientFeatureTransport>()
            .As<IMcpClientFeatureInvoker>()
            .As<IMcpTaskStatusNotifier>()
            .As<IStdioMcpClientFeatureTransport>()
            .InstancePerMatchingLifetimeScope(McpLifetimeScopeTags.Session);

        builder.RegisterInstance(new StdioMcpTransportOptions())
            .SingleInstance();

        builder.RegisterInstance(new StreamableHttpMcpTransportOptions())
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<McpProtectedResourceMetadataProvider>()
            .As<IMcpProtectedResourceMetadataProvider>()
            .SingleInstance();

        builder.Register<IMcpHttpAuthorizationService>(context =>
            {
                var transportOptions = context.Resolve<StreamableHttpMcpTransportOptions>();
                if (!transportOptions.Authorization.Enabled)
                {
                    return new McpNullHttpAuthorizationService();
                }

                return new McpHttpAuthorizationService(
                    transportOptions,
                    context.Resolve<IMcpProtectedResourceMetadataProvider>(),
                    new McpJwtAccessTokenValidator(
                        context.Resolve<IHttpClientFactory>(),
                        transportOptions,
                        context.Resolve<ILogger<McpJwtAccessTokenValidator>>()),
                    context.Resolve<ILogger<McpHttpAuthorizationService>>());
            })
            .As<IMcpHttpAuthorizationService>()
            .SingleInstance();

        builder.RegisterType<StreamableHttpMcpSessionTransport>()
            .As<IStreamableHttpMcpSessionTransport>()
            .InstancePerMatchingLifetimeScope(McpLifetimeScopeTags.Session);

        builder.RegisterType<StreamableHttpMcpSessionContext>()
            .AsSelf()
            .InstancePerMatchingLifetimeScope(McpLifetimeScopeTags.Session);

        builder.RegisterType<StreamableHttpMcpSessionManager>()
            .As<IStreamableHttpMcpSessionManager>()
            .SingleInstance();

        builder.RegisterType<StreamableHttpMcpRequestProcessor>()
            .As<IStreamableHttpMcpRequestProcessor>()
            .SingleInstance();

        builder.RegisterType<StreamableHttpMcpServerService>()
            .As<IHostedService>()
            .SingleInstance();

        builder.RegisterType<StdioMcpServerService>()
            .As<IHostedService>()
            .SingleInstance();
    }
}
