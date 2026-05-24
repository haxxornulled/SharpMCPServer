# AgentRouter Worker Execution Slice

This slice promotes the AgentRouter worker from dequeue-only behavior to generic plugin execution.

## Boundary

The AgentRouter core remains provider-side and plugin-agnostic.

- Application owns the worker use case.
- Domain owns run lifecycle transitions.
- Infrastructure owns queue/persistence adapters.
- Plugins own external execution mechanics.
- MCPServer.Host only composes modules and runs the hosted service.

## Generic capability selection

Queued work is routed to a plugin through metadata:

```text
agent.capability=<capability-name>
```

The worker does not special-case SSH. SSH remains the first practical plugin because it is high-risk and exercises the seam well, but the worker only sees `IAgentPluginRegistry` and `IAgentPlugin`.

## Execution flow

```text
queued work item
  -> load AgentRun snapshot
  -> rehydrate AgentRun aggregate
  -> transition queued -> planning
  -> resolve agent.capability
  -> select IAgentPlugin
  -> transition planning -> working
  -> execute selected plugin
  -> transition working -> completed or failed
  -> persist snapshot and write trace after each transition
```

Business execution failures fail the run and are returned as processed work, not unhandled background-service crashes. Persistence/trace failures still return a failed worker cycle because the supervisor cannot trust the run state.

## Why this matters

This is the first end-to-end path where the hosted AgentRouter can consume queued work and complete a run through the generic plugin seam without becoming SSH-aware.
