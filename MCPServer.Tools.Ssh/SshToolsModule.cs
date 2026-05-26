using Autofac;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Ssh;
using MCPServer.Tools.Ssh.Tools;

namespace MCPServer.Tools.Ssh;

/// <summary>
/// Registers MCP-facing SSH tools over the public SSH provider API.
/// SSH profile storage, credential vaulting, policy, and execution are owned by MCPServer.Ssh.
/// </summary>
public sealed class SshToolsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterModule(new SshProviderModule());

        RegisterTool<SshExecTool>(builder, SshToolNames.Exec);
        RegisterTool<SshProfilesListTool>(builder, SshToolNames.ProfilesList);
        RegisterTool<SshAgentLaunchTool>(builder, SshToolNames.AgentLaunch);
        RegisterTool<SshAgentStatusTool>(builder, SshToolNames.AgentStatus);
        RegisterTool<SshAgentOutputTool>(builder, SshToolNames.AgentOutput);
        RegisterTool<SshAgentCancelTool>(builder, SshToolNames.AgentCancel);
    }

    private static void RegisterTool<TTool>(ContainerBuilder builder, string name)
        where TTool : IMcpTool
    {
        builder.RegisterType<TTool>()
            .Keyed<IMcpTool>(name)
            .As<IMcpTool>()
            .SingleInstance();
    }
}
