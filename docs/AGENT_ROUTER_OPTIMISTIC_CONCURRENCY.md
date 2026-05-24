# AgentRouter Optimistic Concurrency Slice

This slice makes the run store version-aware.

## Rule

`AgentRun` remains the domain authority for state transitions and version increments.
The store is responsible for rejecting stale writes.

A snapshot with `Version == 0` is created with `TryCreateAsync`.
A snapshot with `Version > 0` is updated with:

```text
expectedVersion = snapshot.Version - 1
```

If the persisted snapshot no longer has that expected version, the update fails with a concurrency conflict.

## Why

The hosted AgentRouter eventually supports multiple queue consumers and multiple external plugins. Even with `MaxConcurrentRuns = 1` today, the storage port must not allow two workers or two request paths to overwrite each other silently.

## Boundary

The Application layer owns the persistence port:

```text
IAgentRunStore
  TryCreateAsync
  TryUpdateAsync(expectedVersion)
  GetSnapshotAsync
```

The Infrastructure layer owns the default in-memory adapter:

```text
InMemoryAgentRunStore
```

This is a development/default adapter. A durable adapter later should preserve the same expected-version semantics.
