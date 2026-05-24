# MCP resources spec notes

Target spec: MCP `2025-11-25`.

This server now exposes a minimal resources surface:

- `resources/list`
- `resources/read`
- `resources/templates/list`

The server declares `capabilities.resources` as an empty object. That means the basic resources feature is available, but the optional `subscribe` and `listChanged` features are not advertised. The implementation therefore does not register `resources/subscribe` or emit `notifications/resources/list_changed` / `notifications/resources/updated`.

The built-in resource is:

```text
mcpserver://server/info
```

It is intentionally static and safe. It exists to validate resource discovery/read behavior without creating filesystem or network exposure.

Pagination uses opaque server-owned cursors. Clients must pass returned cursors verbatim and must not parse or guess cursor values.

Resource reads for missing registered resources return JSON-RPC error code `-32002` (`ResourceNotFound`), matching the MCP resources error-handling guidance.

## Resource subscription update

Implemented `resources/subscribe` and `resources/unsubscribe`; `initialize` now advertises `resources.subscribe: true`. Unknown subscription targets map to `-32002` Resource not found. Update notifications are modeled but not emitted by static Phase 1 resources.
