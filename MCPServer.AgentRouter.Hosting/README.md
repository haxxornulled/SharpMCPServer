# MCPServer.AgentRouter.Hosting

This package is the hosting/composition edge for the AgentRouter bounded context.

It is responsible for:
- wiring AgentRouter application and infrastructure services for host use
- exposing hosted-provider composition modules for the outer host
- running background services and routing loops at the host edge

It is **not** responsible for:
- SSH/runtime provider concerns
- MCP tool exposure
- credential/vault ownership
- provider-specific configuration models

The core AgentRouter packages remain provider-neutral. Host composition should flow through hosting-side modules rather than wiring inner AgentRouter modules directly in application entry points.
