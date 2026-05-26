using Autofac;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.Execution.Abstractions.Models;
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
    public async Task AgentRouterBackgroundService_StartingAsync_Runs_Startup_Tasks()
    {
        var startupTask = new TrackingStartupTask("test-startup-task");
        using var container = BuildContainer(new SignalingAgentRouterWorker(), startupTask);
        var lifecycleService = container.Resolve<IHostedLifecycleService>();

        await lifecycleService.StartingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, startupTask.CallCount);
    }

    [Fact]
    public async Task AgentRouterBackgroundService_StartingAsync_Throws_When_Startup_Task_Fails()
    {
        var startupTask = new TrackingStartupTask("failing-startup-task", shouldFail: true);
        using var container = BuildContainer(new SignalingAgentRouterWorker(), startupTask);
        var lifecycleService = container.Resolve<IHostedLifecycleService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycleService.StartingAsync(TestContext.Current.CancellationToken));

        Assert.Contains("failing-startup-task", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, startupTask.CallCount);
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

    private static IContainer BuildContainer(
        SignalingAgentRouterWorker worker,
        params IAgentRouterStartupTask[] startupTasks)
    {
        var builder = new ContainerBuilder();

        builder.RegisterInstance(worker)
            .AsSelf()
            .As<IAgentRouterWorker>()
            .SingleInstance();

        foreach (var startupTask in startupTasks)
        {
            builder.RegisterInstance(startupTask)
                .As<IAgentRouterStartupTask>()
                .SingleInstance();
        }

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

    private sealed class TrackingStartupTask : IAgentRouterStartupTask
    {
        private readonly bool _shouldFail;
        private int _callCount;

        public TrackingStartupTask(string name, bool shouldFail = false)
        {
            Name = name;
            _shouldFail = shouldFail;
        }

        public string Name { get; }

        public int CallCount => Volatile.Read(ref _callCount);

        public ValueTask<Fin<Unit>> ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);

            return _shouldFail
                ? new ValueTask<Fin<Unit>>(Fin.Fail<Unit>(Error.New("startup task failed for test.")))
                : new ValueTask<Fin<Unit>>(Fin.Succ(default(Unit)));
        }
    }
}
