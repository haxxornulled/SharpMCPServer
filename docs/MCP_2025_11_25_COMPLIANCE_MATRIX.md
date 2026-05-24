# MCP 2025-11-25 Compliance Matrix

Current package: Phase 1 stdio server foundation.

Status meanings:

- **Implemented**: code path exists and has unit/protocol coverage.
- **Partial**: protocol model or base behavior exists, but the complete feature is intentionally not finished.
- **Deferred**: outside the current stdio/server-foundation phase.
- **Not advertised**: the server does not claim the capability in `initialize`.

## Base protocol and lifecycle

| Spec Area | Status | Coverage | Notes |
|---|---:|---|---|
| JSON-RPC 2.0 message envelopes | Implemented | Parser, dispatcher, serializer, transcript tests | Requests, notifications, and inbound responses are recognized. Mixed request/response shapes are rejected. Batch input is rejected in Phase 1. |
| Request IDs | Implemented | Parser/unit/protocol tests | Accepts string and integer IDs only. Rejects null, fractional numbers, booleans, arrays, and objects. Reused request IDs are rejected within a session. |
| Error response IDs | Implemented | Serializer/protocol tests | Echoes valid request IDs. Omits `id` when no valid MCP request ID can be read. |
| `params` shape | Implemented | Parser/protocol tests | MCP `params`, when present, must be a JSON object. |
| `_meta` key validation | Implemented | Unit/protocol tests | Validates MCP `_meta` key shape and `_meta.progressToken` type. |
| Lifecycle `initialize` | Implemented | Protocol tests | Initialization is first, required fields are checked, protocol version is negotiated, and client capabilities are recorded. |
| `notifications/initialized` | Implemented | Protocol tests | Normal operation is gated until this notification is received. |
| Version negotiation | Implemented | Initialize handler | Responds with requested supported version or current supported version. |
| Capability negotiation | Implemented | Initialize handler and capability honesty tests | Server advertises only implemented Phase 1 server features. Client feature capabilities are stored but not used unless a feature needs them. |
| Shutdown | Implemented via transport behavior | stdio service | EOF on stdin ends the stdio loop. No protocol shutdown message is defined by MCP. |

## Stdio transport

| Spec Area | Status | Coverage | Notes |
|---|---:|---|---|
| UTF-8 JSON-RPC frames | Implemented | Frame reader and transcript tests | Invalid UTF-8 becomes parse error. |
| Newline-delimited messages | Implemented | Frame reader tests | Strict mode requires newline-delimited frames. CRLF is tolerated and normalized. |
| No embedded newlines in messages | Implemented | Input/output guards | Input rejects embedded raw CR. Output serializer scans frames before writing. |
| stdout contains only MCP messages | Implemented | Serializer guards/docs | Protocol frames are written to stdout; diagnostics go to stderr. |
| Logs to stderr | Implemented | Host configuration | Serilog console sink is configured for stderr. |

## Basic utilities

| Spec Area | Status | Coverage | Notes |
|---|---:|---|---|
| `ping` | Implemented | Protocol tests | Allows absent params or object-shaped `RequestParams`; returns `{}`. |
| Cancellation | Implemented foundation | Handler/registry tests | Accepts `notifications/cancelled`, tracks active requests, and links cancellation tokens. More long-running tool tests belong with real tools. |
| Progress | Partial | Models and `_meta.progressToken` tests | Validates progress tokens and tracks active-token uniqueness. Built-in tools do not yet emit progress notifications. |
| Pagination | Implemented | Tools/resources/prompts tests | Uses opaque server-owned cursors and rejects guessed/invalid cursors. |
| Logging | Implemented foundation | Protocol tests | `logging/setLevel` is implemented. `notifications/message` model/serializer support exists. Application log streaming is not wired until redaction/rate-limit policy exists. |

## Server features

| Spec Area | Advertised | Status | Notes |
|---|---:|---:|---|
| Tools capability | Yes | Implemented | `tools/list`, `tools/call`, descriptor validation, icons, annotations, execution metadata, input/output schemas. `tools.listChanged=false`. |
| Tool input schema validation | Yes | Implemented | Uses JsonSchema.Net 9.2.1 through an Infrastructure adapter. Default/explicit supported dialect is JSON Schema 2020-12. Root schema type must be `object`. |
| Tool output schema validation | Yes | Implemented | Tools with `outputSchema` must return `structuredContent`; structured content is validated against the schema. |
| Resources capability | Yes | Implemented | `resources/list`, `resources/read`, `resources/templates/list`, `resources/subscribe`, `resources/unsubscribe`. `resources.subscribe=true`; `listChanged` omitted. |
| Resource subscriptions | Yes | Implemented foundation | Static built-in resources can be subscribed/unsubscribed. Update notifications are modeled but not emitted for static resources. |
| Prompts capability | Yes | Implemented | `prompts/list`, `prompts/get`, string-only prompt arguments, opaque cursors. `prompts.listChanged=false`. |
| Completion capability | Yes | Implemented foundation | `completion/complete` supports prompt argument completions. Resource-template completions return empty until templates exist. |
| Tasks capability | No | Deferred | Not advertised. Tool `execution.taskSupport` is modeled/validated, but task-augmented operation is not implemented. |
| Experimental capability | No | Deferred | No non-standard features are advertised. |

## Client features

| Spec Area | Status | Notes |
|---|---:|---|
| Roots | Partial foundation | Client capability is stored; `notifications/roots/list_changed` increments revision. Server does not send `roots/list` yet. |
| Sampling | Capability detection only | Not used. Sampling requests require explicit user/security policy before implementation. |
| Elicitation | Capability detection only | Not used. Requires consent and prompt-injection policy before implementation. |
| Client tasks | Capability detection only | Not used. |

## Transports and auth not in Phase 1

| Spec Area | Status | Notes |
|---|---:|---|
| Streamable HTTP transport | Deferred | Requires POST/GET/SSE behavior, sessions, origin validation, protocol version header, resumability choices, and auth boundary. |
| Authorization | Deferred | Required for HTTP deployments. Not part of stdio local transport foundation. |

## Phase 1 remaining watch items

1. Add benchmark baselines after this schema-validation pass is green.
2. Add real long-running tools before claiming cancellation/progress behavior beyond the foundation.
3. Do not advertise list-change notifications until dynamic registries and notification emitters exist.
4. Do not advertise tasks/sampling/elicitation until the policy layer exists.
