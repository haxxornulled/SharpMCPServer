using Autofac;
using Autofac.Extensions.DependencyInjection;
using MCPServer.AgentRouter.Hosting;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.AgentRouter.Infrastructure.Options;
using MCPServer.Host.Composition;
using MCPServer.Infrastructure.Mcp.Http;
using MCPServer.Infrastructure.Mcp.Stdio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http;
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
        .ConfigureServices(services =>
        {
            services.AddHttpClient();
            services.AddHttpClient("MCPServer.AuthorizationDiscovery")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    MaxConnectionsPerServer = 32,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
                });
        })
        .UseServiceProviderFactory(new AutofacServiceProviderFactory())
        .ConfigureContainer<ContainerBuilder>((hostContext, containerBuilder) =>
        {
            var agentRouterSqliteOptions = ReadAgentRouterSqliteOptions(hostContext.Configuration);
            agentRouterSqliteOptions.Validate();
            var streamableHttpOptions = ReadStreamableHttpTransportOptions(hostContext.Configuration);
            streamableHttpOptions.Authorization.Validate();

            containerBuilder.RegisterInstance(agentRouterSqliteOptions)
                .AsSelf()
                .SingleInstance();

            containerBuilder.RegisterInstance(streamableHttpOptions)
                .AsSelf()
                .SingleInstance();

            // Compose the MCP server runtime through a single host-owned module so Program.cs stays
            // focused on host bootstrapping and configuration rather than scattered feature wiring.
            containerBuilder.RegisterModule(new McpServerHostRuntimeModule());

            if (streamableHttpOptions.Enabled)
            {
                containerBuilder.Register(context => context.Resolve<IStreamableHttpMcpSessionTransport>())
                    .As<IMcpClientFeatureInvoker>()
                    .As<IMcpTaskStatusNotifier>()
                    .SingleInstance();

                containerBuilder.RegisterInstance(new MCPServer.Infrastructure.Mcp.Stdio.StdioMcpTransportOptions
                {
                    Enabled = false
                })
                    .AsSelf()
                    .SingleInstance();
            }
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


static AgentRouterSqliteOptions ReadAgentRouterSqliteOptions(IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(configuration);

    var section = configuration.GetSection("AgentRouter:Sqlite");
    var connectionString = section["ConnectionString"];
    var ensureCreatedRaw = section["EnsureCreatedOnUse"];

    return new AgentRouterSqliteOptions
    {
        ConnectionString = string.IsNullOrWhiteSpace(connectionString)
            ? AgentRouterSqliteOptions.DefaultConnectionString
            : connectionString.Trim(),
        EnsureCreatedOnUse = bool.TryParse(ensureCreatedRaw, out var ensureCreated)
            ? ensureCreated
            : AgentRouterSqliteOptions.Default.EnsureCreatedOnUse
    };
}

static StreamableHttpMcpTransportOptions ReadStreamableHttpTransportOptions(IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(configuration);

    var section = configuration.GetSection("McpTransport:Http");
    var enabledRaw = section["Enabled"];
    var portRaw = section["Port"];
    var pathRaw = section["Path"];
    var bindLoopbackOnlyRaw = section["BindLoopbackOnly"];
    var sseRetryMillisecondsRaw = section["SseRetryMilliseconds"];
    var sseHeartbeatMillisecondsRaw = section["SseHeartbeatMilliseconds"];
    var maxSessionHistoryMessagesRaw = section["MaxSessionHistoryMessages"];
    var authorizationSection = section.GetSection("Authorization");
    var authorizationEnabledRaw = authorizationSection["Enabled"];
    var authorizationServers = ReadStringArray(authorizationSection.GetSection("AuthorizationServers"));
    var requiredScopes = ReadStringArray(authorizationSection.GetSection("RequiredScopes"));
    var scopesSupported = ReadStringArray(authorizationSection.GetSection("ScopesSupported"));

    return new StreamableHttpMcpTransportOptions
    {
        Enabled = bool.TryParse(enabledRaw, out var enabled) && enabled,
        Port = int.TryParse(portRaw, out var port) ? port : 0,
        Path = string.IsNullOrWhiteSpace(pathRaw) ? "/mcp/" : pathRaw.Trim(),
        BindLoopbackOnly = string.IsNullOrWhiteSpace(bindLoopbackOnlyRaw) || bool.TryParse(bindLoopbackOnlyRaw, out var bindLoopbackOnly) && bindLoopbackOnly,
        SseRetryMilliseconds = int.TryParse(sseRetryMillisecondsRaw, out var sseRetryMilliseconds) ? sseRetryMilliseconds : 3_000,
        SseHeartbeatMilliseconds = int.TryParse(sseHeartbeatMillisecondsRaw, out var sseHeartbeatMilliseconds) ? sseHeartbeatMilliseconds : 15_000,
        MaxSessionHistoryMessages = int.TryParse(maxSessionHistoryMessagesRaw, out var maxSessionHistoryMessages) && maxSessionHistoryMessages > 0
            ? maxSessionHistoryMessages
            : 256,
        Authorization = new StreamableHttpMcpAuthorizationOptions
        {
            Enabled = bool.TryParse(authorizationEnabledRaw, out var authorizationEnabled) && authorizationEnabled,
            AuthorizationServers = authorizationServers,
            RequiredScopes = requiredScopes,
            ScopesSupported = scopesSupported
        }
    };
}

static IReadOnlyList<string> ReadStringArray(IConfigurationSection section)
{
    ArgumentNullException.ThrowIfNull(section);

    var values = new List<string>();
    foreach (var child in section.GetChildren())
    {
        if (string.IsNullOrWhiteSpace(child.Value))
        {
            continue;
        }

        values.Add(child.Value.Trim());
    }

    return values;
}
