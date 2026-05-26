# Host Composition Boundary

`MCPServer.Host` owns application-edge composition for the main MCP server process.

Rules:

- `Program.cs` should remain focused on bootstrapping, configuration, and lifetime.
- Host runtime wiring belongs in host-owned composition modules.
- The host should not manually scatter AgentRouter, SSH, or tool registration logic across the entry point.
- Provider-specific runtime details should be hidden behind host-owned modules, not leaked into the bootstrap path.

Current direction:

- `McpServerHostRuntimeModule` is the single host-owned composition module.
- `AgentRouterHostedProviderModule` hides AgentRouter application/infrastructure/hosting registration details.
- `MCPServer.Host` should avoid direct references to inner AgentRouter implementation projects except through host-owned composition.
