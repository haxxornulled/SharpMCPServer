# AgentRouter Concurrency Model

The AgentRouter is hosted as a singleton lifecycle service, but the service must not create arbitrary fire-and-forget work.

This slice introduces the first concurrency boundary:

```text
AgentRouterBackgroundService
  -> IAgentRouterWorker
    -> IAgentRunQueue
      -> bounded Channel<AgentRunWorkItem>
```

## Rules

- The queue is bounded.
- Queue capacity is explicit through `AgentRouterConcurrencyOptions.MaxQueuedRuns`.
- Queue-full behavior is explicit through `AgentRunQueueFullModes.Wait` or `AgentRunQueueFullModes.Reject`.
- The initial safe default is `MaxConcurrentRuns = 1`.
- The worker dequeues one work item per cycle.
- Planning and plugin execution remain disabled in this slice.
- SSH is not executed by the queue or worker yet.

## Clean Architecture boundary

`IAgentRunQueue` is an Application port.

`BoundedChannelAgentRunQueue` is an Infrastructure adapter.

The Domain layer remains responsible for run lifecycle rules. The queue only transports work items; it does not mutate `AgentRun` state.

## Next slices

The next concurrency slices should add:

1. Optimistic concurrency to `IAgentRunStore`.
2. A domain `AgentExecutionLease` concept.
3. Worker-side state transitions from queued -> planning -> working.
4. Controlled plugin execution through `IAgentPluginRegistry`.
