using System.Threading.Channels;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Options;
using MCPServer.AgentRouter.Application.WorkItems;

namespace MCPServer.AgentRouter.Infrastructure.Queues;

public sealed class BoundedChannelAgentRunQueue : IAgentRunQueue
{
    private readonly Channel<AgentRunWorkItem> _channel;
    private readonly AgentRouterConcurrencyOptions _options;
    private int _count;

    public BoundedChannelAgentRunQueue(AgentRouterConcurrencyOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        _channel = Channel.CreateBounded<AgentRunWorkItem>(new BoundedChannelOptions(_options.MaxQueuedRuns)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = _options.MaxConcurrentRuns == 1,
            SingleWriter = false
        });
    }

    public int Count => Volatile.Read(ref _count);

    public async ValueTask<Fin<Unit>> EnqueueAsync(
        AgentRunWorkItem workItem,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!workItem.IsValid)
        {
            return Fin.Fail<Unit>(Error.New("agent run work item requires a valid run id and objective."));
        }

        if (string.Equals(_options.QueueFullMode, AgentRunQueueFullModes.Reject, StringComparison.Ordinal))
        {
            if (!_channel.Writer.TryWrite(workItem))
            {
                return Fin.Fail<Unit>(Error.New("AgentRouter run queue is full."));
            }

            Interlocked.Increment(ref _count);
            return Fin.Succ(default(Unit));
        }

        await _channel.Writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _count);
        return Fin.Succ(default(Unit));
    }

    public async ValueTask<Fin<AgentRunWorkItem>> DequeueAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var workItem = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Decrement(ref _count);
            return Fin.Succ(workItem);
        }
        catch (ChannelClosedException exception)
        {
            return Fin.Fail<AgentRunWorkItem>(Error.New(exception.Message));
        }
    }
}
