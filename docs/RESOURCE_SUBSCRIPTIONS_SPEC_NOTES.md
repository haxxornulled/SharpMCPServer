# Resource Subscriptions Spec Notes

MCP 2025-11-25 defines resource subscriptions as an optional server feature. When enabled, the server declares `capabilities.resources.subscribe: true`, accepts `resources/subscribe`, accepts `resources/unsubscribe`, and may emit `notifications/resources/updated` for subscribed resources when content changes.

Current implementation:

- Advertises `resources.subscribe: true` during `initialize`.
- Implements `resources/subscribe` and `resources/unsubscribe`.
- Validates that subscription URIs are syntactically valid MCP resource URIs.
- Validates that subscription URIs refer to known resources.
- Returns an empty result object on successful subscribe/unsubscribe.
- Returns `-32002` when the target resource is unknown.
- Tracks subscriptions in an in-memory session-scoped registry.
- Does not emit `notifications/resources/updated` yet because the built-in Phase 1 resources are static.

The notification method and parameter model are present so dynamic resource providers can emit spec-shaped update notifications later without changing public domain contracts.
