# MCP Client Features Notes

MCP defines some capabilities that clients expose to servers rather than server features returned to clients.

For `2025-11-25`, this project now records the client's declared capability shape during `initialize`:

- `roots`
- `sampling`
- `elicitation`
- `tasks`

The server does not advertise these as server capabilities. They are stored as session facts so future tools/resources can check whether a client supports a feature before attempting server-initiated requests.

## Roots

Clients that support roots declare `capabilities.roots`. If `roots.listChanged` is true, the client may send `notifications/roots/list_changed` when workspace roots change.

Current behavior:

- `initialize` validates that `roots` is object-shaped when supplied.
- `roots.listChanged`, when supplied, must be boolean.
- `notifications/roots/list_changed` is accepted after the session is ready.
- The notification produces no JSON-RPC response.
- The session roots revision is incremented for future cache invalidation.

The project does not yet issue `roots/list` requests to the client. That requires a server-to-client request coordinator and response correlation layer, which should be implemented before roots-dependent tools are added.

## Sampling and elicitation

The server records whether the client declared `sampling` and `elicitation`, but it does not yet send `sampling/createMessage` or `elicitation/create` requests. Those require explicit policy gates because the MCP spec calls out user approval and sensitive-data controls.

## Tasks

The server records whether the client declared task utilities. Server-side task augmentation is still not advertised and not implemented. Per the tasks spec, a receiver that does not declare task augmentation for a request type processes the request normally and ignores task augmentation metadata for that request type.
