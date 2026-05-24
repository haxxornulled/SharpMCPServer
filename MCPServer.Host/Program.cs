using Autofac;
using Autofac.Extensions.DependencyInjection;
using MCPServer.AgentRouter.Application;
using MCPServer.AgentRouter.Defaults;
using MCPServer.AgentRouter.Hosting;
using MCPServer.AgentRouter.Infrastructure;
using MCPServer.AgentRouter.Ssh;
using MCPServer.Application;
using MCPServer.Infrastructure;
using MCPServer.Tools.Ssh;
using MCPServer.Tools.Ssh.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
    .CreateLogger();

try
{
    Log.Information("Starting MCP server host");

    var builder = Host.CreateDefaultBuilder(args)
        // GUI MCP clients often launch the server with their own working directory.
        // Keep configuration rooted beside the executable so appsettings.json is loaded
        // consistently whether the server is started from Visual Studio, PowerShell,
        // LM Studio, or the Host Sidecar stdio proxy.
        .UseContentRoot(AppContext.BaseDirectory)
        .ConfigureLogging(loggingBuilder => loggingBuilder.ClearProviders())
        .UseSerilog()
        .ConfigureServices((hostContext, services) =>
        {
            services.AddOptions<SshToolSettings>()
                .Bind(hostContext.Configuration.GetSection(SshToolSettings.ConfigurationSectionName));
        })
        .UseServiceProviderFactory(new AutofacServiceProviderFactory())
        .ConfigureContainer<ContainerBuilder>((hostContext, containerBuilder) =>
        {
            containerBuilder.RegisterModule(new ApplicationModule());
            containerBuilder.RegisterModule(new AgentRouterApplicationModule());
            containerBuilder.RegisterModule(new AgentRouterInfrastructureModule());
            containerBuilder.RegisterModule(new AgentRouterDefaultsModule());
            containerBuilder.RegisterModule(new AgentRouterHostingModule());
            containerBuilder.RegisterModule(new InfrastructureModule());

            var initialSshSettings = SshToolSettings.FromConfiguration(
                hostContext.Configuration.GetSection(SshToolSettings.ConfigurationSectionName));

            Log.Information(
                "MCP SSH tools initial configuration: enabled={Enabled}, profilePath={ProfilePath}, contentRoot={ContentRoot}",
                initialSshSettings.Enabled,
                string.IsNullOrWhiteSpace(initialSshSettings.ProfilePath) ? "<default>" : initialSshSettings.ProfilePath,
                hostContext.HostingEnvironment.ContentRootPath);

            // Register SSH tools unconditionally. Runtime policy reads IOptionsMonitor<SshToolSettings>,
            // so appsettings changes made by a future VS extension can enable/disable SSH execution,
            // adjust allowlists, trace paths, and profile paths without restarting this MCP server.
            containerBuilder.RegisterModule(new SshToolsModule());
            containerBuilder.RegisterModule(new AgentRouterSshModule());
        });

    using var host = builder.Build();
    await host.RunAsync().ConfigureAwait(false);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}
