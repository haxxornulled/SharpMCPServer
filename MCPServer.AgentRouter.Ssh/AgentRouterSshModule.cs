using Autofac;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Ssh.Services;

namespace MCPServer.AgentRouter.Ssh;

public sealed class AgentRouterSshModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterType<SshAgentPlugin>()
            .AsSelf()
            .As<IAgentPlugin>()
            .SingleInstance();
    }
}
