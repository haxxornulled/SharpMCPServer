using Autofac;
using Autofac.Extensions.DependencyInjection;
using MCPServer.AgentRouter.Infrastructure.Options;
using MCPServer.Host.Configuration;
using MCPServer.Host.Composition;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Application.Options;
using MCPServer.Inference.Infrastructure.Options;
using MCPServer.Infrastructure.Mcp.Http;
using MCPServer.Ssh.Configuration;
using MCPServer.Workspace.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
            services.AddHttpClient("lmstudio")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    MaxConnectionsPerServer = 32,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
                });
            services.AddHttpClient("ollama")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    MaxConnectionsPerServer = 32,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
                });
            services.AddHttpClient("anthropic")
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
            var inferenceRoutingOptions = ReadInferenceRoutingOptions(hostContext.Configuration);
            inferenceRoutingOptions.Validate();
            var inferenceOptions = ReadInferenceOptions(hostContext.Configuration);
            inferenceOptions.Validate();
            var streamableHttpOptions = ReadStreamableHttpTransportOptions(hostContext.Configuration);
            streamableHttpOptions.Authorization.Validate();
            var workspaceOptions = ReadWorkspaceOptions(hostContext.Configuration);
            workspaceOptions.Validate();

            containerBuilder.RegisterInstance(agentRouterSqliteOptions)
                .AsSelf()
                .SingleInstance();

            containerBuilder.RegisterInstance(inferenceRoutingOptions)
                .AsSelf()
                .SingleInstance();

            containerBuilder.RegisterInstance(inferenceOptions)
                .AsSelf()
                .SingleInstance();

            containerBuilder.RegisterInstance(streamableHttpOptions)
                .AsSelf()
                .SingleInstance();

            containerBuilder.RegisterInstance(workspaceOptions)
                .AsSelf()
                .SingleInstance();

            containerBuilder.RegisterInstance(hostContext.Configuration)
                .As<IConfiguration>()
                .SingleInstance();

            var sshToolSettings = SshToolSettings.Normalize(SshToolSettings.FromConfiguration(hostContext.Configuration.GetSection(SshToolSettings.ConfigurationSectionName)));
            Log.Information("SSH tools enabled in host configuration: {Enabled}", sshToolSettings.Enabled);
            containerBuilder.RegisterInstance(new StaticOptionsMonitor<SshToolSettings>(sshToolSettings))
                .As<IOptionsMonitor<SshToolSettings>>()
                .SingleInstance();

            // Compose the MCP server runtime through a single host-owned module so Program.cs stays
            // focused on host bootstrapping and configuration rather than scattered feature wiring.
            containerBuilder.RegisterModule(new McpServerHostRuntimeModule());

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
        Enabled = enabledRaw is null || bool.TryParse(enabledRaw, out var enabled) && enabled,
        Port = int.TryParse(portRaw, out var port) && port > 0 ? port : StreamableHttpMcpTransportOptions.DefaultLoopbackPort,
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

static InferenceRoutingOptions ReadInferenceRoutingOptions(IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(configuration);

    var section = configuration.GetSection("McpInference:Routing");
    var options = new InferenceRoutingOptions();

    var strategyRaw = section["DefaultStrategy"];
    if (!string.IsNullOrWhiteSpace(strategyRaw) && Enum.TryParse<InferenceRoutingStrategy>(strategyRaw.Trim(), ignoreCase: true, out var strategy))
    {
        options.DefaultStrategy = strategy;
    }

    var maxConcurrentRequestsPerProviderRaw = section["MaxConcurrentRequestsPerProvider"];
    if (int.TryParse(maxConcurrentRequestsPerProviderRaw, out var maxConcurrentRequestsPerProvider) && maxConcurrentRequestsPerProvider > 0)
    {
        options.MaxConcurrentRequestsPerProvider = maxConcurrentRequestsPerProvider;
    }

    var maxFanOutCandidatesRaw = section["MaxFanOutCandidates"];
    if (int.TryParse(maxFanOutCandidatesRaw, out var maxFanOutCandidates) && maxFanOutCandidates > 0)
    {
        options.MaxFanOutCandidates = maxFanOutCandidates;
    }

    return options;
}

static McpInferenceOptions ReadInferenceOptions(IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(configuration);

    var section = configuration.GetSection(McpInferenceOptions.ConfigurationSectionName);
    var options = new McpInferenceOptions();
    var providersSection = section.GetSection("Providers");

    foreach (var providerSection in providersSection.GetChildren())
    {
        var providerId = providerSection.Key?.Trim();
        if (string.IsNullOrWhiteSpace(providerId))
        {
            continue;
        }

        var providerOptions = new McpInferenceProviderOptions
        {
            Enabled = bool.TryParse(providerSection["Enabled"], out var enabled) && enabled,
            BaseAddress = providerSection["BaseAddress"] is { Length: > 0 } baseAddress ? baseAddress.Trim() : string.Empty,
            Model = providerSection["Model"] is { Length: > 0 } model ? model.Trim() : string.Empty,
            HttpClientName = providerSection["HttpClientName"] is { Length: > 0 } httpClientName ? httpClientName.Trim() : string.Empty,
            ApiKey = providerSection["ApiKey"] is { Length: > 0 } apiKey ? apiKey.Trim() : string.Empty,
            AnthropicVersion = providerSection["AnthropicVersion"] is { Length: > 0 } anthropicVersion ? anthropicVersion.Trim() : "2023-06-01"
        };

        options.Providers[providerId] = providerOptions;
    }

    return options;
}

static McpWorkspaceOptions ReadWorkspaceOptions(IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(configuration);

    var section = configuration.GetSection(McpWorkspaceOptions.ConfigurationSectionName);
    var options = new McpWorkspaceOptions();

    var approvalToken = section["ApprovalToken"];
    if (!string.IsNullOrWhiteSpace(approvalToken))
    {
        options.ApprovalToken = approvalToken.Trim();
    }

    var maxReadBytesRaw = section["MaxReadBytes"];
    if (int.TryParse(maxReadBytesRaw, out var maxReadBytes) && maxReadBytes > 0)
    {
        options.MaxReadBytes = maxReadBytes;
    }

    var maxSearchFilesRaw = section["MaxSearchFiles"];
    if (int.TryParse(maxSearchFilesRaw, out var maxSearchFiles) && maxSearchFiles > 0)
    {
        options.MaxSearchFiles = maxSearchFiles;
    }

    var maxSearchResultsRaw = section["MaxSearchResults"];
    if (int.TryParse(maxSearchResultsRaw, out var maxSearchResults) && maxSearchResults > 0)
    {
        options.MaxSearchResults = maxSearchResults;
    }

    var maxPatchBytesRaw = section["MaxPatchBytes"];
    if (int.TryParse(maxPatchBytesRaw, out var maxPatchBytes) && maxPatchBytes > 0)
    {
        options.MaxPatchBytes = maxPatchBytes;
    }

    var sqliteSection = section.GetSection("Sqlite");
    var sqliteDatabasePath = sqliteSection["DatabasePath"];
    if (!string.IsNullOrWhiteSpace(sqliteDatabasePath))
    {
        options.Sqlite.DatabasePath = sqliteDatabasePath.Trim();
    }

    var sqliteEnsureCreatedOnUseRaw = sqliteSection["EnsureCreatedOnUse"];
    if (bool.TryParse(sqliteEnsureCreatedOnUseRaw, out var sqliteEnsureCreatedOnUse))
    {
        options.Sqlite.EnsureCreatedOnUse = sqliteEnsureCreatedOnUse;
    }

    var excludedDirectoryNames = ReadStringArray(section.GetSection("ExcludedDirectoryNames"));
    if (excludedDirectoryNames.Count > 0)
    {
        options.ExcludedDirectoryNames = excludedDirectoryNames.ToList();
    }

    options.Roots = ReadWorkspaceRoots(section.GetSection("Roots"));
    return options;
}

static List<McpWorkspaceRootOptions> ReadWorkspaceRoots(IConfigurationSection section)
{
    ArgumentNullException.ThrowIfNull(section);

    var roots = new List<McpWorkspaceRootOptions>();
    foreach (var child in section.GetChildren())
    {
        var name = child["Name"];
        var path = child["Path"];
        var allowWriteRaw = child["AllowWrite"];

        var root = new McpWorkspaceRootOptions
        {
            Name = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim(),
            Path = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim(),
            AllowWrite = !bool.TryParse(allowWriteRaw, out var allowWrite) || allowWrite
        };

        roots.Add(root);
    }

    return roots;
}
