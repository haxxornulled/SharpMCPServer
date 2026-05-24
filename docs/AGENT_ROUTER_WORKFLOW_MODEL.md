# AgentRouter Workflow Model

AgentRouter supports two workflow modes. The mode is a domain concept, not an infrastructure detail.

## Deterministic workflow

A deterministic workflow is appropriate when the execution path is known before the run starts.

Example:

```text
validate -> approve -> execute SSH command -> collect output -> complete
```

The route, capability, approval requirement, and completion behavior should be explicit. This is the preferred mode for high-risk provider plugins such as SSH until the agentic loop is proven safe.

## Agentic workflow

An agentic workflow is appropriate when the run must dynamically plan, select a capability, execute, inspect the result, and decide whether to continue.

Example:

```text
objective -> plan -> select capability -> execute -> inspect -> continue/stop
```

Agentic mode should still obey the same domain lifecycle, concurrency, approval, and trace rules as deterministic mode.

## Boundary rule

The Domain layer owns workflow mode semantics and run lifecycle invariants. Application coordinates use cases. Infrastructure adapts queues, storage, SSH, MCP, filesystem, and other external systems.
