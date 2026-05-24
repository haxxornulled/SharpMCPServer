# AgentRouter Clean Architecture Boundary

The AgentRouter is a packageable bounded context. It uses Clean Architecture / Hexagonal layering inside that bounded context instead of being buried in the existing MCP server application or SSH tool package.

## Projects

```text
MCPServer.AgentRouter.Abstractions
MCPServer.AgentRouter.Domain
MCPServer.AgentRouter.Application
MCPServer.AgentRouter.Infrastructure
MCPServer.AgentRouter.Defaults
MCPServer.AgentRouter.Defaults.Tests
```

## Dependency direction

```text
Abstractions     no project references
Domain           no project references
Application      -> Abstractions, Domain
Infrastructure   -> Application, Domain
Defaults         -> Abstractions
Host             composes packages, but does not own AgentRouter internals
```

## Layer responsibilities

### Abstractions

Public package seam for provider/router/route selection. Third-party router packages should be able to implement these contracts without referencing Host, Application, Infrastructure, or SSH internals.

### Domain

Pure AgentRouter concepts: objectives, run identity, run status, plan steps, policy decisions, and risk labels. No Autofac, no hosting, no SSH, no filesystem, no MCP transport.

### Application

Use-case ports and orchestration contracts: run coordinator, planner, policy evaluator, tool executor, and trace writer. This layer defines what the AgentRouter needs from the outside world.

### Infrastructure

Adapters for the ports defined by Application. Later slices can add MCP client/session adapters, filesystem trace adapters, model profile adapters, and adapters that call the existing SSH MCP tools. This layer must not contain core policy decisions.

### Defaults

Default provider package and Autofac registrations for the current safe default route/no-op router. Defaults is not the application core.

## Important rule

The SSH tool pack remains an execution backend. The AgentRouter coordinates objectives and ports; it does not become the SSH tool pack.
