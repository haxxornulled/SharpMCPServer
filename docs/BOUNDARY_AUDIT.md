# AgentRouter Boundary Audit

## Scope

The boundary audit covers only the AgentRouter core packages:

- `MCPServer.AgentRouter.Abstractions`
- `MCPServer.AgentRouter.Domain`
- `MCPServer.AgentRouter.Application`
- `MCPServer.AgentRouter.Infrastructure`
- `MCPServer.AgentRouter.Hosting`

## Rule

AgentRouter core must remain provider-neutral.

That means no provider-specific nouns or ownership for:

- SSH
- credentials
- vaults
- path resolvers
- tool adapters
- provider settings

## Current result

This pass removes the last misleading provider-specific example text from the AgentRouter infrastructure README and leaves the core projects provider-neutral by naming and intent.

## Follow-up discipline

Run the boundary verification script before broad refactors:

- `pwsh ./scripts/Verify-AgentRouterBoundary.ps1`

Treat any new provider-specific noun found under the AgentRouter core packages as a boundary regression unless there is an explicit architectural exception documented in the repo.

## Host / Sidecar composition edge audit

- `MCPServer.Host` composes through `McpServerHostRuntimeModule` rather than wiring AgentRouter feature modules directly in `Program.cs`.
- `MCPServer.Host.Sidecar` composes through `SshHostSidecarRuntimeModule` and `SshHostSidecarRuntimeFactory` rather than owning a second SSH runtime/persistence subsystem.
- `scripts/Verify-HostCompositionBoundary.ps1` checks host and high-level sidecar edge-boundary rules after each small refactor.
- `scripts/Verify-SidecarCompositionBoundary.ps1` checks sidecar factory/module-specific composition guardrails after each sidecar refactor.


## Execution / SSH package boundary audit

- `MCPServer.Execution.Abstractions` remains provider-neutral and should not accumulate SSH nouns.
- `MCPServer.ExecutionPlugins.Ssh` is the SSH-backed adapter from the provider-neutral execution seam into `MCPServer.Ssh`.
- `MCPServer.Tools.Ssh` remains the MCP adapter surface only and should not re-own provider runtime/storage logic.
- `scripts/Verify-ExecutionBoundary.ps1` checks execution seam neutrality and prevents AgentRouter core from depending on SSH execution plugin/provider details.
- `scripts/Verify-SshPackageBoundary.ps1` checks that Tools.Ssh stays an MCP adapter and that the SSH provider does not pick up generic agent plugin concerns.
- `scripts/Verify-Boundaries.ps1` runs all boundary verifiers together.


## MCP Task Parity Batch

- Added baseline `tasks/list`, `tasks/get`, `tasks/result`, and `tasks/cancel` handlers.
- Added in-memory task registry and task capability advertising.
- Added verifier coverage for task parity files.


## Server-Initiated Client Feature Runtime

Implemented across both the stdio and Streamable HTTP edges through `IMcpClientFeatureInvoker`, `StdioMcpClientFeatureTransport`, and `StreamableHttpMcpSessionTransport`, with tool entry points for `client.sample`, `client.elicit.form`, and `client.elicit.url`.

Task status notifications are also published through the same runtime boundary so that task-augmented requests stay observable across both transports.

See [docs/SPEC_COMPLIANCE.md](SPEC_COMPLIANCE.md) for the current protocol implementation matrix before adding more boundary cases.
