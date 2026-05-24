using Autofac;
using LanguageExt;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Hosting.Services;
using Xunit;

namespace MCPServer.AgentRouter.Hosting.Tests.Services;

public sealed class AgentRouterBackgroundServiceTests
{
    [Fact]
    public void AgentRouterHostingModule_Resolves_IHostedService()
    {
        using var container = BuildContainer(new SignalingAgentRouterWorker());

        var hostedService = container.Resolve<IHostedService>();

        Assert.IsType<AgentRouterBackgroundService>(hostedService);
    }

    [Fact]
    public void AgentRouterHostingModule_Resolves_IHostedLifecycleService()
    {
        using var container = BuildContainer(new SignalingAgentRouterWorker());

        var lifecycleService = container.Resolve<IHostedLifecycleService>();

        Assert.IsType<AgentRouterBackgroundService>(lifecycleService);
    }

    [Fact]
    public async Task AgentRouterBackgroundService_Lifecycle_Methods_Are_NoOp_Hooks()
    {
        using var container = BuildContainer(new SignalingAgentRouterWorker());
        var lifecycleService = container.Resolve<IHostedLifecycleService>();

        await lifecycleService.StartingAsync(TestContext.Current.CancellationToken);
        await lifecycleService.StartedAsync(TestContext.Current.CancellationToken);
        await lifecycleService.StoppingAsync(TestContext.Current.CancellationToken);
        await lifecycleService.StoppedAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AgentRouterBackgroundService_Runs_Worker_Cycle()
    {
        var worker = new SignalingAgentRouterWorker();
        using var container = BuildContainer(worker);
        var hostedService = container.Resolve<IHostedService>();

        await hostedService.StartAsync(TestContext.Current.CancellationToken);
        await worker.CycleRan.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await hostedService.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(worker.CallCount >= 1);
    }

    [Fact]
    public void AgentRouterHosting_Does_Not_Reference_MCPServer_Runtime_Layers()
    {
        var forbiddenAssemblyNames = new[]
        {
            "MCPServer.Host",
            "MCPServer.Application",
            "MCPServer.Domain",
            "MCPServer.Infrastructure",
            "MCPServer.Tools.Ssh",
            "MCPServer.UnitTests"
        };

        var referencedNames = typeof(AgentRouterBackgroundService)
            .Assembly
            .GetReferencedAssemblies()
            .Select(static assemblyName => assemblyName.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var forbiddenName in forbiddenAssemblyNames)
        {
            Assert.DoesNotContain(forbiddenName, referencedNames);
        }
    }

    private static IContainer BuildContainer(SignalingAgentRouterWorker worker)
    {
        var builder = new ContainerBuilder();

        builder.RegisterInstance(worker)
            .AsSelf()
            .As<IAgentRouterWorker>()
            .SingleInstance();

        builder.RegisterInstance(new AgentRouterBackgroundServiceOptions
            {
                IdleDelay = TimeSpan.FromMilliseconds(25),
                RunImmediately = true
            })
            .AsSelf()
            .SingleInstance();

        builder.RegisterInstance(NullLogger<AgentRouterBackgroundService>.Instance)
            .As<ILogger<AgentRouterBackgroundService>>()
            .SingleInstance();

        builder.RegisterModule(new AgentRouterHostingModule());

        return builder.Build();
    }

    private sealed class SignalingAgentRouterWorker : IAgentRouterWorker
    {
        private readonly TaskCompletionSource _cycleRan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task CycleRan => _cycleRan.Task;

        public ValueTask<Fin<AgentRouterWorkerCycleResult>> RunCycleAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Interlocked.Increment(ref _callCount);
            _cycleRan.TrySetResult();

            var result = AgentRouterWorkerCycleResult.Idle("test cycle complete");
            return new ValueTask<Fin<AgentRouterWorkerCycleResult>>(Fin.Succ(result));
        }
    }
}
