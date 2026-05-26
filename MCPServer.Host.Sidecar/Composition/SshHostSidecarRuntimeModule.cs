using Autofac;
using MCPServer.Ssh;
using MCPServer.Ssh.Configuration;
using Microsoft.Extensions.Options;

namespace MCPServer.Host.Sidecar.Composition;

public sealed class SshHostSidecarRuntimeModule : Module
{
    private readonly SshToolSettings _settings;

    public SshHostSidecarRuntimeModule(SshToolSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterInstance(new StaticOptionsMonitor<SshToolSettings>(_settings))
            .As<IOptionsMonitor<SshToolSettings>>()
            .SingleInstance();

        builder.RegisterModule(new SshProviderModule());
    }
}
