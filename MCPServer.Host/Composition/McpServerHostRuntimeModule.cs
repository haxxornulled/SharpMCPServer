using Autofac;
using MCPServer.AgentRouter.Hosting;
using MCPServer.Application;
using MCPServer.ExecutionPlugins.Ssh;
using MCPServer.Infrastructure;
using MCPServer.Tools.Ssh;

namespace MCPServer.Host.Composition;

public sealed class McpServerHostRuntimeModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterModule(new ApplicationModule());
        builder.RegisterModule(new AgentRouterHostedProviderModule());
        builder.RegisterModule(new InfrastructureModule());
        builder.RegisterModule(new SshToolsModule());
        builder.RegisterModule(new ExecutionPluginsSshModule());
    }
}
