using Autofac;
using MCPServer.Ssh;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.ExecutionPlugins.Ssh.Services;
using MCPServer.Execution.Abstractions;

namespace MCPServer.ExecutionPlugins.Ssh;

public sealed class ExecutionPluginsSshModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterModule(new SshProviderModule());

        builder.RegisterType<SshAgentPlugin>()
            .AsSelf()
            .As<IAgentPlugin>()
            .SingleInstance();
    }
}
