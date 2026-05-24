using Autofac;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Tools.Ssh.Infrastructure;
using MCPServer.Tools.Ssh.Interfaces;
using MCPServer.Tools.Ssh.Services;
using MCPServer.Tools.Ssh.Tools;

namespace MCPServer.Tools.Ssh;

public sealed class SshToolsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterType<DefaultSshPathResolver>()
            .As<ISshPathResolver>()
            .SingleInstance();

        builder.RegisterType<FileSystemSshProfileStore>()
            .As<ISshProfileStore>()
            .SingleInstance();

        builder.RegisterType<FileSystemSshExecutionTraceWriter>()
            .As<ISshExecutionTraceWriter>()
            .SingleInstance();

        builder.RegisterType<SshExecutionPolicy>()
            .As<ISshExecutionPolicy>()
            .SingleInstance();

        builder.RegisterType<SshNetCommandExecutor>()
            .As<ISshCommandExecutor>()
            .SingleInstance();

        builder.RegisterType<SshExecutionService>()
            .As<ISshExecutionService>()
            .SingleInstance();

        builder.RegisterType<SshAgentRuntime>()
            .As<ISshAgentRuntime>()
            .SingleInstance();

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
