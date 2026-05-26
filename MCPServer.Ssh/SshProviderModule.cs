using Autofac;
using MCPServer.Ssh.Configuration;
using MCPServer.Ssh.Infrastructure;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Services;
using MCPServer.Ssh.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MCPServer.Ssh;

/// <summary>
/// Registers the public SSH provider runtime: profile store, credential vault,
/// command executor, policy, trace writer, and long-running SSH agent runtime.
/// This module intentionally contains no MCP tool registrations.
/// </summary>
public sealed class SshProviderModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Register(context =>
            {
                var configuration = context.ResolveOptional<IConfiguration>();
                var settings = configuration is null
                    ? new SshToolSettings()
                    : SshToolSettings.FromConfiguration(configuration.GetSection(SshToolSettings.ConfigurationSectionName));

                return new StaticOptionsMonitor<SshToolSettings>(SshToolSettings.Normalize(settings));
            })
            .As<IOptionsMonitor<SshToolSettings>>()
            .SingleInstance()
            .IfNotRegistered(typeof(IOptionsMonitor<SshToolSettings>));

        builder.RegisterType<DefaultSshPathResolver>()
            .As<ISshPathResolver>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<FileSystemSshProfileStore>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SqliteSshProfileStore>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SqliteSshCredentialVault>()
            .As<ISshCredentialVault>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<DefaultSshCredentialResolver>()
            .As<ISshCredentialResolver>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.Register(context =>
            {
                var settings = SshToolSettings.Normalize(context.Resolve<IOptionsMonitor<SshToolSettings>>().CurrentValue);
                return settings.ProfileStoreKind switch
                {
                    var profileStoreKind when profileStoreKind.Equals(SshProfileStoreKinds.Json, StringComparison.OrdinalIgnoreCase)
                        => (ISshProfileStore)context.Resolve<FileSystemSshProfileStore>(),
                    _ => context.Resolve<SqliteSshProfileStore>()
                };
            })
            .As<ISshProfileStore>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.Register(context => context.Resolve<SqliteSshProfileStore>())
            .As<ISshProfileManagementStore>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<FileSystemSshExecutionTraceWriter>()
            .As<ISshExecutionTraceWriter>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<SshExecutionPolicy>()
            .As<ISshExecutionPolicy>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<SshNetCommandExecutor>()
            .As<ISshCommandExecutor>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<SshExecutionService>()
            .As<ISshExecutionService>()
            .SingleInstance()
            .PreserveExistingDefaults();

        builder.RegisterType<SshAgentRuntime>()
            .As<ISshAgentRuntime>()
            .SingleInstance()
            .PreserveExistingDefaults();
    }
}
