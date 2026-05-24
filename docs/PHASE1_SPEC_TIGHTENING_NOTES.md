# Phase 1 Spec-Tightening Notes

This pass closes additional protocol-shape gaps against MCP `2025-11-25` without adding heavyweight feature breadth.

## Base protocol

- JSON-RPC method names beginning with `rpc.` are rejected before handler dispatch. That namespace is reserved by JSON-RPC.
- `_meta`, when inspected for request progress, must be an object.
- `_meta.progressToken` must be a string or integer.
- Active progress tokens are tracked and must be unique across in-flight requests.

## JSON Schema dialect policy

MCP defaults schemas without `$schema` to JSON Schema 2020-12. Phase 1 therefore supports:

- no `$schema` field, treated as 2020-12
- `https://json-schema.org/draft/2020-12/schema`
- `https://json-schema.org/draft/2020-12/schema#`

Other explicit dialects are rejected gracefully. Full JSON Schema validation is now handled by the Infrastructure JsonSchema.Net adapter for tool input/output schemas while unsupported dialect declarations are still rejected by project policy.

## Tools

Tool descriptors now carry the current spec-shape fields:

- `name`
- `title`
- `description`
- `icons`
- `inputSchema`
- `outputSchema`
- `annotations`
- `execution.taskSupport`

The static `server.info` tool declares an `outputSchema`, returns `structuredContent`, and the `tools/call` path validates structured output when a tool advertises an output schema.


## Spec Tightening Update

- Inbound JSON-RPC response messages are now recognized and ignored; this server does not emit responses to responses.
- `initialize` now validates required fields before session state is marked initialized.
- `ping` now rejects supplied params because the MCP ping request is defined with no parameters.
- Tool results with `structuredContent` now place the serialized JSON into a text content block for backward compatibility.

## 2025-11-25 metadata and icon tightening pass

- Added `_meta` key syntax validation for incoming request parameters before dispatch.
- Invalid `_meta` on requests now returns `-32602` Invalid params; invalid `_meta` on notifications remains no-response fire-and-forget behavior.
- Preserved `progressToken` handling as the only interpreted MCP `_meta` key in Phase 1.
- Added tool icon validation so emitted descriptors do not contain empty or unsafe icon sources and only advertise the MIME/theme values documented by MCP icon metadata.
- Added a protocol-level guard that `tools/call.arguments`, when present, is a JSON object, matching `CallToolRequestParams.arguments?: { [key: string]: unknown }`.

## Pagination cursor tightening

`tools/list` now emits opaque server-owned cursor strings instead of transparent integer offsets. This keeps the implementation aligned with the MCP pagination model: clients must treat cursors as opaque tokens, should not parse or modify them, and invalid cursors return `-32602` Invalid params.

The cursor currently encodes the next index plus the static tool-list size and is scoped to the static Phase 1 tool registry. Dynamic tool registration remains disabled because `tools.listChanged` is advertised as `false`.
