# Prompts Spec Notes — MCP 2025-11-25

This project now implements the server-side prompt surface for MCP 2025-11-25.

Implemented:

- `prompts` capability in `initialize`.
- `prompts/list` with server-owned opaque cursor pagination.
- `prompts/get` with string-only argument object validation.
- `notifications/prompts/list_changed` method constant for future dynamic prompt catalogs.
- Application-owned prompt ports under `MCPServer.Application/Mcp/Interfaces`.
- Autofac keyed prompt registration by prompt name.
- Built-in safe prompt: `server.status`.

Current capability declaration uses `listChanged = false` because the prompt catalog is static. Dynamic prompt registration should not flip this to `true` until the server can emit `notifications/prompts/list_changed` safely.

Prompt `arguments` are validated before prompt execution. The current built-in prompt accepts only an optional string argument named `focus`.
