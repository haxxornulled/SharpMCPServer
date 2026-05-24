using Autofac;
using LanguageExt;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Options;
using MCPServer.AgentRouter.Application.WorkItems;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Infrastructure.Queues;
using MCPServer.AgentRouter.Infrastructure.Tests.Testing;
using Xunit;

namespace MCPServer.AgentRouter.Infrastructure.Tests.Queues;

public sealed class BoundedChannelAgentRunQueueTests
{
    [Fact]
    public void AgentRouterInfrastructureModule_Resolves_Run_Queue()
    {
        using var container = BuildContainer(AgentRouterConcurrencyOptions.Default);

        var queue = container.Resolve<IAgentRunQueue>();

        Assert.IsType<BoundedChannelAgentRunQueue>(queue);
    }

    [Fact]
    public async Task EnqueueAsync_Then_DequeueAsync_Returns_Work_Item()
    {
        var queue = new BoundedChannelAgentRunQueue(AgentRouterConcurrencyOptions.Default);
        var workItem = CreateWorkItem("execute deterministic SSH validation workflow");

        TestFin.Success(await queue.EnqueueAsync(workItem, TestContext.Current.CancellationToken));
        var dequeued = TestFin.Success(await queue.DequeueAsync(TestContext.Current.CancellationToken));

        Assert.Equal(workItem.RunId, dequeued.RunId);
        Assert.Equal(workItem.Objective, dequeued.Objective);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task EnqueueAsync_Rejects_When_Queue_Is_Full_And_Mode_Is_Reject()
    {
        var options = new AgentRouterConcurrencyOptions
        {
            MaxQueuedRuns = 1,
            MaxConcurrentRuns = 1,
            MaxConcurrentStepsPerRun = 1,
            QueueFullMode = AgentRunQueueFullModes.Reject
        };
        var queue = new BoundedChannelAgentRunQueue(options);

        TestFin.Success(await queue.EnqueueAsync(CreateWorkItem("first queued run"), TestContext.Current.CancellationToken));
        var failure = TestFin.Failure(await queue.EnqueueAsync(CreateWorkItem("second queued run"), TestContext.Current.CancellationToken));

        Assert.Contains("queue is full", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Constructor_Rejects_Invalid_Concurrency_Options()
    {
        var options = new AgentRouterConcurrencyOptions
        {
            MaxQueuedRuns = 0
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedChannelAgentRunQueue(options));
    }

    [Fact]
    public void AgentRouterInfrastructure_Does_Not_Reference_MCPServer_Runtime_Layers()
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

        var referencedNames = typeof(BoundedChannelAgentRunQueue)
            .Assembly
            .GetReferencedAssemblies()
            .Select(static assemblyName => assemblyName.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var forbiddenName in forbiddenAssemblyNames)
        {
            Assert.DoesNotContain(forbiddenName, referencedNames);
        }
    }

    private static IContainer BuildContainer(AgentRouterConcurrencyOptions options)
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(options)
            .AsSelf()
            .SingleInstance();
        builder.RegisterModule(new AgentRouterInfrastructureModule());
        return builder.Build();
    }

    private static AgentRunWorkItem CreateWorkItem(string objectiveText)
    {
        Assert.True(AgentObjective.TryCreate(objectiveText, out var objective));

        return new AgentRunWorkItem(
            AgentRunId.New(),
            objective,
            new Dictionary<string, string?>(capacity: 0),
            DateTimeOffset.UtcNow);
    }
}
