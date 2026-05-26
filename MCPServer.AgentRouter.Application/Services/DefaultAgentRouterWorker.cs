using LanguageExt;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.Execution.Abstractions.Models;
using MCPServer.AgentRouter.Application.WorkItems;

namespace MCPServer.AgentRouter.Application.Services;

public sealed class DefaultAgentRouterWorker : IAgentRouterWorker
{
    private const string IdleMessage = "AgentRouter background worker is running. No AgentRouter run queue is registered in this composition.";

    private readonly IAgentRunExecutor _runExecutor;
    private readonly IAgentRunQueue? _runQueue;

    public DefaultAgentRouterWorker(
        IAgentRunExecutor runExecutor,
        IEnumerable<IAgentRunQueue> runQueues)
    {
        _runExecutor = runExecutor ?? throw new ArgumentNullException(nameof(runExecutor));
        ArgumentNullException.ThrowIfNull(runQueues);
        _runQueue = runQueues.FirstOrDefault();
    }

    public ValueTask<Fin<AgentRouterWorkerCycleResult>> RunCycleAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_runQueue is null)
        {
            var result = AgentRouterWorkerCycleResult.Idle(IdleMessage);
            return new ValueTask<Fin<AgentRouterWorkerCycleResult>>(Fin.Succ(result));
        }

        return RunQueuedCycleAsync(_runQueue, cancellationToken);
    }

    private async ValueTask<Fin<AgentRouterWorkerCycleResult>> RunQueuedCycleAsync(
        IAgentRunQueue runQueue,
        CancellationToken cancellationToken)
    {
        var dequeueResult = await runQueue.DequeueAsync(cancellationToken).ConfigureAwait(false);

        return await dequeueResult.Match(
            Succ: workItem => _runExecutor.ExecuteAsync(workItem, cancellationToken),
            Fail: static error => new ValueTask<Fin<AgentRouterWorkerCycleResult>>(Fin.Fail<AgentRouterWorkerCycleResult>(error)))
            .ConfigureAwait(false);
    }
}
