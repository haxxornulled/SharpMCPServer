# AgentRouter SQLite Persistence

The first durable AgentRouter store is SQLite-backed and lives in `MCPServer.AgentRouter.Infrastructure`.

## Boundary

`MCPServer.AgentRouter.Application` still depends only on the `IAgentRunStore` and `IAgentTraceWriter` ports.
The SQLite implementation is an Infrastructure adapter:

```text
Application
  -> IAgentRunStore
  -> IAgentTraceWriter

Infrastructure
  -> SqliteAgentRunStore
  -> SqliteAgentTraceWriter
```

## Transaction model

Each create, update, and trace append operation opens a short-lived EF Core DbContext and an explicit transaction.

`SqliteAgentRunStore.TryUpdateAsync` uses an atomic version-checked update:

```text
WHERE run_id = @runId AND version = @expectedVersion
```

That preserves the optimistic-concurrency behavior required by the AgentRouter worker and approval-resume paths.

## Default registration

`AgentRouterInfrastructureModule` now registers SQLite as the default `IAgentRunStore` and `IAgentTraceWriter`.
The in-memory adapters remain available as concrete types for tests or explicitly overridden dev scenarios.

Default connection string:

```text
Data Source=agent-router.db;Cache=Shared
```

Override by registering `AgentRouterSqliteOptions` before loading the Infrastructure module.

## Dapr

Dapr Workflow remains a valid later adapter for distributed workflow orchestration. It should not replace the local store contract yet.
The SQLite adapter proves our own domain/application contracts first.
