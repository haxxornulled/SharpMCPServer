# Architecture Refactor Task List

## Target shape

Core AgentRouter:
- MCPServer.AgentRouter.Abstractions
- MCPServer.AgentRouter.Domain
- MCPServer.AgentRouter.Application
- MCPServer.AgentRouter.Infrastructure
- MCPServer.AgentRouter.Hosting

Provider-neutral execution seam:
- MCPServer.Execution.Abstractions

SSH provider and adapters:
- MCPServer.Ssh
- MCPServer.ExecutionPlugins.Ssh
- MCPServer.Tools.Ssh

Composition/admin:
- MCPServer.Host
- MCPServer.Host.Sidecar

## Completed in this pass
- Removed MCPServer.AgentRouter.Defaults project.
- Removed MCPServer.AgentRouter.Ssh project.
- Moved default/no-op router composition into MCPServer.AgentRouter.Hosting.
- Introduced MCPServer.Execution.Abstractions and moved plugin execution contracts there.
- Introduced MCPServer.ExecutionPlugins.Ssh and moved SSH-backed execution plugin there.
- Updated host and tests to reference the new packages.

## Remaining work
- Verify no remaining provider-specific nouns/references exist under MCPServer.AgentRouter.* core.
- Review Host and Host.Sidecar composition for any further edge-only provider binding cleanup.
- Keep the repo docs aligned with the current stable MCP spec matrix in [docs/SPEC_COMPLIANCE.md](SPEC_COMPLIANCE.md).


## Safe next-step discipline

- Keep broad project moves off the table until each small compile-first patch is green.
- Run `pwsh ./scripts/Verify-AgentRouterBoundary.ps1` after any AgentRouter refactor.
- Prefer docs/scripts/boundary-audit changes over model moves unless the tree is green first.


## Green-first guardrails

- Use `scripts/Verify-AgentRouterBoundary.ps1` after AgentRouter/package-shape changes.
- Use `scripts/Verify-HostCompositionBoundary.ps1` after host or high-level sidecar composition changes.
- Use `scripts/Verify-SidecarCompositionBoundary.ps1` after sidecar factory/module changes.
- Use `scripts/Verify-ExecutionBoundary.ps1` after execution seam or plugin changes.
- Use `scripts/Verify-SshPackageBoundary.ps1` after SSH provider or MCP adapter changes.
- Use `scripts/Verify-Boundaries.ps1` when batching several safe boundary/documentation changes together.
- Prefer one compile-safe step per zip over broad multi-project moves.


## MCP Task Parity Batch

- Added baseline `tasks/list`, `tasks/get`, `tasks/result`, and `tasks/cancel` handlers.
- Added in-memory task registry and task capability advertising.
- Added verifier coverage for task parity files.

## Current runtime coverage

- Server-initiated sampling and elicitation are implemented across stdio and Streamable HTTP.
- Task status notifications are emitted by the server-side task registry and carried by the client transport layers.
- Streamable HTTP transport and authorization / OIDC handling are implemented and exercised by the live smoke tests.
- Refer to [docs/SPEC_COMPLIANCE.md](SPEC_COMPLIANCE.md) for the current protocol matrix before adding new runtime surfaces.
