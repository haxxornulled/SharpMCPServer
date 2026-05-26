# Repo Architecture

## Current shape

Core AgentRouter packages:
- `MCPServer.AgentRouter.Abstractions`
- `MCPServer.AgentRouter.Domain`
- `MCPServer.AgentRouter.Application`
- `MCPServer.AgentRouter.Infrastructure`
- `MCPServer.AgentRouter.Hosting`

Provider-neutral execution seam:
- `MCPServer.Execution.Abstractions`

Host-side shared domain:
- `MCPServer.Domain`
- `MCPServer.Application` consumes the shared host-side domain model

Workspace editing and sandboxes:
- `MCPServer.Workspace`
- `MCPServer.Tools.Workspace`

Client runtime and bridge:
- `MCPServer.Client`
- `MCPServer.Client.Infrastructure`
- `MCPServer.Client.Console`
- `MCPServer.AgentRouter.PythonBridge.Native`
- `python/`

SSH provider/runtime and adapters:
- `MCPServer.Ssh`
- `MCPServer.ExecutionPlugins.Ssh`
- `MCPServer.Tools.Ssh`

Composition/admin:
- `MCPServer.Host`
- `MCPServer.Host.Sidecar`

## Boundary rules

- AgentRouter core is provider-neutral.
- `MCPServer.AgentRouter.Defaults` does not exist.
- `MCPServer.AgentRouter.Ssh` does not exist.
- Default/no-op router composition lives in `MCPServer.AgentRouter.Hosting`.
- `MCPServer.Client` stays protocol-facing and reusable; browser launch, loopback auth flow, and other implementation details live in `MCPServer.Client.Infrastructure` or the composition root.
- `MCPServer.Workspace` owns workspace roots, sandbox persistence, and file policy; `MCPServer.Tools.Workspace` is the MCP adapter surface only.
- Workspace sandboxes are persisted in SQLite and are transport-agnostic, so stdio and HTTP see the same state when they use the same database path.
- SSH execution integration lives outside the AgentRouter namespace in `MCPServer.ExecutionPlugins.Ssh`.
- `MCPServer.Ssh` owns SSH runtime, policy, profile storage, and credential vault behavior.
- `MCPServer.Tools.Ssh` owns MCP SSH tool exposure only.
