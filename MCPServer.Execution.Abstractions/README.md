# MCPServer.Execution.Abstractions

This package defines the provider-neutral execution seam used by AgentRouter.

It is responsible for:
- generic execution/plugin contracts
- execution request/result models that do not assume SSH or any other provider
- shared abstractions that allow new execution providers to plug in without changing AgentRouter core packages

It is **not** responsible for:
- SSH-specific behavior
- credentials, vaults, or path resolution
- MCP tool adapters
- host composition

New execution providers should depend on this package, not on AgentRouter core internals.
