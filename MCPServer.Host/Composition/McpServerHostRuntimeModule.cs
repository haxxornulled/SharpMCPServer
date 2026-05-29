using Autofac;
using MCPServer.Inference.Application;
using MCPServer.Inference.Infrastructure;
using MCPServer.AgentRouter.Hosting;
using MCPServer.Application;
using MCPServer.Application.Mcp;
using MCPServer.ExecutionPlugins.Ssh;
using MCPServer.Infrastructure;
using MCPServer.Tools.Inference;
using MCPServer.Tools.Ssh;
using MCPServer.Tools.Workspace;

namespace MCPServer.Host.Composition;

public sealed class McpServerHostRuntimeModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterModule(new InferenceApplicationModule());
        builder.RegisterModule(new InferenceInfrastructureModule());
        builder.RegisterModule(new ApplicationModule());
        builder.RegisterModule(new AgentRouterHostedProviderModule());
        builder.RegisterModule(new AgentRouterToolsModule());
        builder.RegisterModule(new InfrastructureModule());
        builder.RegisterModule(new SshToolsModule());
        builder.RegisterModule(new InferenceToolsModule());
        builder.RegisterModule(new WorkspaceToolsModule());
        builder.RegisterModule(new ExecutionPluginsSshModule());
    }
}
