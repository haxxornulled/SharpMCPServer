using Autofac;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Hosting;
using MCPServer.AgentRouter.Infrastructure.Options;
using MCPServer.ExecutionPlugins.Ssh;
using MCPServer.Tools.Ssh;
using MCPServer.Ssh.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using MCPServer.Execution.Abstractions;

namespace MCPServer.AgentRouter.Hosting.Tests.Composition;

public sealed class AgentRouterHostCompositionTests
{
    [Fact]
    public async Task AgentRouter_Modules_Compose_As_Hosted_Provider_With_Sqlite_And_Ssh_Plugin()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "agent-router-composition-" + Guid.NewGuid().ToString("N") + ".db");

        await using var cleanup = new TempFileCleanup(databasePath);

        var builder = new ContainerBuilder();
        RegisterHostLikeAgentRouterComposition(builder, databasePath);

        await using var container = builder.Build();

        var hostedLifecycleService = container.Resolve<IHostedLifecycleService>();
        var hostedService = container.Resolve<IHostedService>();
        var startupTasks = container.Resolve<IEnumerable<IAgentRouterStartupTask>>().ToArray();
        var plugins = container.Resolve<IEnumerable<IAgentPlugin>>().ToArray();

        Assert.Same(hostedLifecycleService, hostedService);
        Assert.Contains(startupTasks, static task => string.Equals(task.Name, "sqlite-database-initializer", StringComparison.Ordinal));
        Assert.Contains(plugins, static plugin => string.Equals(plugin.Name, "ssh", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(container.Resolve<IAgentRunCoordinator>());
        Assert.NotNull(container.Resolve<IAgentRunQueue>());
        Assert.NotNull(container.Resolve<IAgentRunStore>());
        Assert.NotNull(container.Resolve<IAgentTraceWriter>());
        Assert.NotNull(container.Resolve<IAgentPluginRegistry>());
        Assert.NotNull(container.Resolve<IAgentRouterProvider>());

        await hostedLifecycleService.StartingAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(databasePath));
    }

    private static void RegisterHostLikeAgentRouterComposition(ContainerBuilder builder, string databasePath)
    {
        builder.RegisterGeneric(typeof(NullLogger<>))
            .As(typeof(ILogger<>))
            .SingleInstance();

        builder.RegisterInstance(
                new StaticOptionsMonitor<SshToolSettings>(new SshToolSettings
                {
                    Enabled = true,
                    RequireExplicitProfileAllowlist = true,
                    AllowUnknownHostKeys = false,
                    AllowShellInterpreterInlineCommands = false
                }))
            .As<IOptionsMonitor<SshToolSettings>>()
            .SingleInstance();

        builder.RegisterInstance(new AgentRouterSqliteOptions
            {
                ConnectionString = $"Data Source={databasePath};Cache=Shared;Pooling=False",
                EnsureCreatedOnUse = true
            })
            .AsSelf()
            .SingleInstance();

        builder.RegisterModule(new AgentRouterHostedProviderModule());
        builder.RegisterModule(new SshToolsModule());
        builder.RegisterModule(new ExecutionPluginsSshModule());
    }

    private sealed class StaticOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        private readonly TOptions _value;

        public StaticOptionsMonitor(TOptions value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public TOptions CurrentValue => _value;

        public TOptions Get(string? name)
        {
            return _value;
        }

        public IDisposable OnChange(Action<TOptions, string?> listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            return NullDisposable.Instance;
        }
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        private NullDisposable()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TempFileCleanup : IAsyncDisposable
    {
        private readonly string _path;

        public TempFileCleanup(string path)
        {
            _path = path;
        }

        public async ValueTask DisposeAsync()
        {
            await DeleteIfExistsAsync(_path);
            await DeleteIfExistsAsync(_path + "-wal");
            await DeleteIfExistsAsync(_path + "-shm");
        }

        private static async ValueTask DeleteIfExistsAsync(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            IOException? lastException = null;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.Delete(path);
                    return;
                }
                catch (IOException ex)
                {
                    lastException = ex;
                    await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
                }
            }

            if (lastException is not null)
            {
                throw lastException;
            }

            throw new IOException($"Could not delete temporary file '{path}'.");
        }
    }
}
