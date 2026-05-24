# MCP Server Implementation Task List

Target specification: Model Context Protocol `2025-11-25`.

This task list is stored with the project so implementation work can be tracked alongside code. Keep this file updated as phases move from planned to in progress to complete.

## Status Legend

- `[ ]` Not started
- `[~]` In progress
- `[x]` Complete

## Phase 1 - Protocol Core and stdio Foundation

Goal: create the minimum viable MCP server foundation before adding real application tools.

### Protocol primitives

- `[x]` Use built-in `System.Text.Json` only; do not add Newtonsoft.Json.
- `[x]` Add JSON-RPC 2.0 protocol constants.
- `[x]` Add MCP protocol version constant for `2025-11-25`.
- `[x]` Add JSON-RPC error code constants.
- `[x]` Add JSON-RPC message envelope model preserving original request IDs.
- `[x]` Convert hot-path JSON-RPC ID/message/response/error envelopes to readonly structs where lifetime-safe.
- `[x]` Add JSON-RPC response and error DTOs.
- `[x]` Add parser for newline-delimited UTF-8 JSON-RPC messages.
- `[x]` Add response serializer using manual `Utf8JsonWriter` emission to avoid object-polymorphic reflection on responses.
- `[x]` Add batch-message rejection/handling decision. MCP examples use single messages; stdio framing rejects arrays in Phase 1.
- `[ ]` Add full protocol schema conformance tests against the official schema reference.
- `[x]` Add Phase 1 MCP compliance matrix document.

### Lifecycle

- `[x]` Add `initialize` handler.
- `[x]` Add protocol-version negotiation for supported version `2025-11-25`.
- `[x]` Add server capability response.
- `[x]` Add server implementation metadata response.
- `[x]` Add `notifications/initialized` handling.
- `[x]` Add session-state tracking for initialization and ready phases.
- `[x]` Block non-`initialize` and non-`ping` requests before initialization completes.
- `[x]` Add configurable request timeout policy.
- `[~]` Add graceful shutdown behavior tests.
- `[x]` Add transcript-level protocol tests for lifecycle, notifications, tools, logging, malformed JSON, bad IDs, and unknown methods.

### Base utilities

- `[x]` Add `ping` handler.
- `[x]` Add `notifications/cancelled` handling and request cancellation map.
- `[x]` Add progress-token model and `notifications/progress` emission support.
- `[x]` Add structured MCP logging notification support.

### stdio transport

- `[x]` Add hosted stdio transport service.
- `[x]` Read newline-delimited JSON-RPC messages from stdin.
- `[x]` Write only JSON-RPC/MCP messages to stdout.
- `[x]` Route Serilog console output to stderr so stdout remains protocol-clean.
- `[x]` Add malformed-message tests for parse errors.
- `[x]` Add fixture-driven initialize/ping/tools smoke tests.
- `[x]` Add pooled stdio frame-reader tests for LF, CRLF, EOF without newline, and oversized-frame recovery.

### Minimal tools capability

- `[x]` Add tool descriptor model.
- `[x]` Add tool registry abstraction.
- `[x]` Add `tools/list` handler.
- `[x]` Add `tools/call` handler.
- `[x]` Add safe `server.info` smoke-test tool.
- `[x]` Add lightweight JSON Schema validation for tool arguments. Phase 1 supports the hot-path subset used by tool input schemas: `type`, `required`, `properties`, `additionalProperties`, `items`, `enum`, string length, array length, and numeric min/max.
- `[x]` Add tool-name validation using MCP name guidance.
- `[x]` Add cursor pagination for `tools/list` using opaque server-owned cursor tokens.
- `[x]` Keep `notifications/tools/list_changed` disabled in Phase 1 by declaring `tools.listChanged=false`; dynamic tool registration is deferred.

### Developer quality gate

- `[x]` Replace `IServiceCollection` application/infrastructure registration extensions with Autofac modules.
- `[x]` Add Autofac package references and wire the Generic Host through `AutofacServiceProviderFactory`.
- `[x]` Use Autofac keyed service lookup for JSON-RPC method dispatch instead of a hand-built handler dictionary. This is service selection, not a replacement for the host service provider factory.
- `[x]` Use Autofac keyed service lookup for tool selection by MCP tool name.
- `[x]` Add `System.Text.Json` source-generation context for known MCP DTOs and disable reflection-based JSON serialization by default.
- `[x]` Cache tool descriptors at registry construction instead of rebuilding descriptor arrays on every `tools/list`.
- `[x]` Enable runtime performance switches in `Directory.Build.props`: tiered PGO, server GC, latest language/analyzer level.
- `[x]` Avoid MediatR and Microsoft.Extensions.DependencyInjection registration patterns in project code.
- `[x]` Add latest `LanguageExt.Core` v5 prerelease line for `Fin<T>`-based recoverable failures.
- `[x]` Replace nullable/exception-only parser and handler failure flow with `Fin<T>` where protocol/application operations can fail.
- `[x]` Add explicit stdio transport session lifetime using both `IDisposable` and `IAsyncDisposable`.
- `[x]` Add unit test project.
- `[x]` Add tests for JSON-RPC parser and serializer.
- `[x]` Add tests for dispatcher request/notification behavior.
- `[x]` Add tests for lifecycle ordering.
- `[x]` Add tests for `tools/list` and `tools/call`.
- `[x]` Add CI-friendly command documentation.

## Phase 2 - Harden Protocol Behavior

Goal: make the protocol implementation robust enough to survive hostile or malformed clients.

- `[x]` Add strict JSON-RPC request validation for Phase 1. Parser/dispatcher covers version, method, MCP ID shape, duplicate request IDs, batch rejection, object-shaped params, lifecycle, MCP notification/request confusion, and unsupported method errors; full generated-schema fixture validation remains later hardening.
- `[~]` Add invalid request handling for missing `jsonrpc`, wrong version, missing method, bad ID shape, and invalid params. Existing dispatcher/parser behavior is covered by unit tests; more fixture coverage remains Phase 2.
- `[x]` Add response-message handling or explicit ignore policy for client responses. Server-side Phase 1 ignores notifications/responses without a method and reports malformed method-less messages with IDs.
- `[x]` Add cancellation map keyed by request ID.
- `[x]` Add request timeout and maximum execution timeout support.
- `[x]` Add max input line length to protect stdio memory usage.
- `[x]` Add max serialized response length guard.
- `[ ]` Add complete structured error data model.
- `[x]` Add transcript harness tests with scripted MCP conversations.
- `[ ]` Add spawned-process stdio integration tests.

## Phase 3 - Real Tool Framework

Goal: make tools safe, discoverable, testable, and easy to add.

- `[ ]` Add tool metadata validation at registration time.
- `[ ]` Add JSON Schema validation package and validator wrapper.
- `[ ]` Add per-tool risk metadata and policy gates.
- `[ ]` Add per-tool timeout configuration.
- `[ ]` Add per-tool output limits.
- `[ ]` Add structured content support.
- `[ ]` Add output schema validation for structured results.
- `[ ]` Add tool annotations support, treating annotations as untrusted metadata.
- `[ ]` Add test helper for invoking tools directly and through JSON-RPC.

## Phase 4 - Resources

Goal: expose controlled, read-only contextual data.

- `[ ]` Add `resources` capability model.
- `[ ]` Add `resources/list`.
- `[ ]` Add `resources/read`.
- `[ ]` Add URI validation and allowed-scheme policy.
- `[ ]` Add MIME type handling.
- `[ ]` Add embedded text/blob resource content models.
- `[ ]` Add cursor pagination.
- `[ ]` Add resource subscription support only if needed.
- `[ ]` Add `notifications/resources/list_changed` and resource update notifications only if dynamic resources are enabled.

## Phase 5 - Prompts

Goal: support reusable prompt/workflow templates.

- `[ ]` Add `prompts` capability model.
- `[ ]` Add prompt descriptor model.
- `[ ]` Add `prompts/list`.
- `[ ]` Add `prompts/get`.
- `[ ]` Add argument validation.
- `[ ]` Add cursor pagination.
- `[ ]` Add `notifications/prompts/list_changed` only if dynamic prompts are enabled.

## Phase 6 - Completion

Goal: support argument autocompletion for prompts/resources where useful.

- `[x]` Add `completions` capability model.
- `[ ]` Add `completion/complete` handler.
- `[ ]` Add completion provider abstraction.
- `[ ]` Add tests for bounded result size and deterministic ordering.

## Phase 7 - Streamable HTTP Transport

Goal: add network transport after stdio is correct.

- `[ ]` Add ASP.NET Core host mode for MCP endpoint.
- `[ ]` Implement single MCP endpoint supporting HTTP POST.
- `[ ]` Support `application/json` responses.
- `[ ]` Evaluate SSE support for streaming responses and server-to-client notifications.
- `[ ]` Add `MCP-Protocol-Version` header enforcement after initialization.
- `[ ]` Add secure session ID generation if sessions are enabled.
- `[ ]` Validate `Origin` headers to mitigate DNS rebinding attacks.
- `[ ]` Bind localhost by default for local servers.
- `[ ]` Add authentication hooks before enabling non-local listeners.

## Phase 8 - Authorization and Security

Goal: put real guardrails around arbitrary tool execution and data access.

- `[ ]` Define tool risk taxonomy.
- `[ ]` Add allow/deny policy for tools.
- `[ ]` Add consent/audit abstraction for destructive or external operations.
- `[ ]` Add secret-redaction policy for logs and responses.
- `[ ]` Add least-privilege filesystem and process execution policies.
- `[ ]` Add security README and operational hardening notes.
- `[ ]` Add threat model document.

## Phase 9 - Advanced Client Features

Goal: support optional MCP features only when the server has a real use case.

- `[ ]` Add roots support if tools/resources need client-provided workspace boundaries.
- `[ ]` Add sampling request support only behind explicit policy gates.
- `[ ]` Add elicitation support only behind explicit user-consent gates.
- `[ ]` Add experimental tasks only after base request/response execution is stable.

## Phase 10 - Packaging and Developer Experience

Goal: make the server easy to run under MCP-capable hosts and easy to develop locally.

- `[ ]` Add sample MCP client configuration for stdio launch.
- `[ ]` Add local smoke-test script.
- `[x]` Add Phase 1 acceptance checklist.
- `[x]` Add protocol harness documentation.
- `[x]` Add README quickstart.
- `[x]` Add protocol transcript examples.
- `[x]` Add troubleshooting guide for stdout/stderr/logging issues.
- `[ ]` Add release checklist.

## Current Phase 1 Notes

Phase 1 has been started. The current implementation provides a protocol-clean stdio service, basic lifecycle negotiation, ping, structured logging capability with `logging/setLevel`, and a minimal tools surface with a safe `server.info` tool. The Host is wired through `AutofacServiceProviderFactory`; runtime method and tool selection uses Autofac keyed services. The hot path now uses source-generated `System.Text.Json` metadata, readonly struct JSON-RPC envelopes, `Fin<T>` for recoverable protocol/application failures with explicit `Fin.Succ<T>` / `Fin.Fail<T>` construction without static Prelude imports, manual `Utf8JsonWriter` response emission, cached tool descriptors, explicit stdio resource disposal, and a configured input-size guard. The unit-test project now covers parser/serializer smoke paths, lifecycle, tools, and logging/setLevel. Full official schema conformance tests are still pending; Phase 1 now includes lightweight tool argument validation, max output-frame protection, a protocol transcript test project, a compliance matrix, and an acceptance checklist.

- [x] Replace string-based stdio reads with pooled UTF-8 frame buffers.
- [x] Replace per-response `ArrayBufferWriter<byte>` with a pooled `IBufferWriter<byte>`.
- [x] Parse JSON-RPC directly from UTF-8 memory instead of allocating an intermediate string.

## Phase 1 pooling update

The stdio transport now rents input/read/frame buffers from `ArrayPool<byte>`, parses JSON-RPC from `ReadOnlyMemory<byte>`, and serializes responses through a pooled `IBufferWriter<byte>`. This keeps the protocol hot path byte-oriented and avoids line-string allocation.


## Phase 1 continuation update

The rest of Phase 1 now includes request execution tracking, configurable request timeouts, `notifications/cancelled` handling, progress-notification serialization support, stricter lifecycle gates, MCP tool-name validation, cursor-aware `tools/list`, stricter base-protocol validation, and an xUnit v3 test project with parser/serializer/dispatcher/lifecycle/tool smoke coverage. Cancellation follows the MCP rule that cancelled in-flight requests should stop processing and avoid sending a late response where possible; the current stdio loop is still intentionally simple, so deeper concurrent request execution remains a Phase 2 hardening topic.

## CI-friendly commands

Use these from the solution root after installing the .NET 10 SDK:

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

xUnit v3 test projects are executable projects, so both test projects are configured with `<OutputType>Exe</OutputType>`.


## Phase 1 stdio transport drilldown

- [x] Drill into 2025-11-25 stdio transport requirements.
- [x] Require newline-delimited frames by default.
- [x] Reject embedded carriage returns after CRLF normalization.
- [x] Add invalid UTF-8 transcript coverage.
- [x] Add output-frame no-embedded-newline guard.
- [x] Document stdout/stderr and newline-delimited constraints in `docs/STDIO_TRANSPORT_SPEC_DRILLDOWN.md`.


## Phase 1 base protocol hardening update

- [x] Enforce MCP request ID shape: string or integer number only; no `null`, booleans, objects, arrays, or fractional numbers.
- [x] Track client request IDs and reject reuse within a session.
- [x] Enforce object-shaped `params` when present.
- [x] Reject MCP `notifications/*` methods sent with an `id`; they must not be dispatched as requests or mutate notification-only lifecycle state.
- [x] Treat empty stdio input frames as invalid MCP messages instead of silently ignoring them.

## Phase 1 spec-tightening continuation

- [x] Track and validate `_meta.progressToken` on incoming requests; accepted values are string or integer, and active tokens are kept unique across in-flight requests.
- [x] Reject invalid `_meta` shapes before dispatching handlers.
- [x] Reject JSON-RPC method names that start with the reserved `rpc.` namespace.
- [x] Add explicit tool descriptor support for `icons`, `annotations`, and `execution.taskSupport`.
- [x] Validate `execution.taskSupport` values at tool registration time: `forbidden`, `optional`, or `required`.
- [x] Validate tool `inputSchema` and `outputSchema` dialect declarations. Phase 1 supports default JSON Schema 2020-12 and explicit `https://json-schema.org/draft/2020-12/schema`.
- [x] Add `server.info` output schema and validate structured tool output when a descriptor declares `outputSchema`.


## Spec Tightening Update

- Inbound JSON-RPC response messages are now recognized and ignored; this server does not emit responses to responses.
- `initialize` now validates required fields before session state is marked initialized.
- `ping` now rejects supplied params because the MCP ping request is defined with no parameters.
- Tool results with `structuredContent` now place the serialized JSON into a text content block for backward compatibility.

## Phase 4 resources foundation update

- [x] Add `resources` capability declaration with no optional subscription/listChanged support enabled yet.
- [x] Add resource descriptor/content models for `resources/list`, `resources/read`, and `resources/templates/list`.
- [x] Add `IMcpResource` and `IMcpResourceRegistry` abstractions.
- [x] Add Autofac keyed resource lookup by URI.
- [x] Add static `mcpserver://server/info` resource for protocol smoke testing.
- [x] Add cursor pagination for `resources/list` using opaque server-owned cursor tokens.
- [x] Add empty `resources/templates/list` support so clients get a valid MCP result instead of method-not-found when no parameterized resources are registered.
- [x] Add resource URI and icon metadata validation at registration time.


## Clean Architecture boundary cleanup

- [x] Moved JSON-RPC parser/serializer ports from Infrastructure to Application.
- [x] Left concrete parser/serializer implementations in Infrastructure.
- [x] Left stdio transport/session/pooling implementation in Infrastructure.

## Resource subscription update

Implemented `resources/subscribe` and `resources/unsubscribe`; `initialize` now advertises `resources.subscribe: true`. Unknown subscription targets map to `-32002` Resource not found. Update notifications are modeled but not emitted by static Phase 1 resources.


## Client capability / roots foundation update

- Captures client capability declarations from `initialize` into session state.
- Validates known client capability entries (`roots`, `sampling`, `elicitation`, `tasks`) are object-shaped when supplied.
- Tracks `roots.listChanged` and accepts `notifications/roots/list_changed` without a response, incrementing the roots revision so future roots caches can invalidate cleanly.
- Does not advertise roots as a server capability; roots are a client feature used only when a future tool/resource needs client-provided workspace boundaries.

## Phase 1 compliance hardening update: JSON Schema and capability honesty

- [x] Normalize the compliance matrix to current code instead of historical append-only status.
- [x] Replace the limited hand-written tool argument schema subset with a real JSON Schema adapter.
- [x] Keep JSON Schema implementation in Infrastructure behind Application port `IMcpToolArgumentValidator`.
- [x] Add `JsonSchema.Net` 9.2.1 for System.Text.Json-based JSON Schema validation.
- [x] Validate tool schemas at registration time.
- [x] Validate tool arguments before execution.
- [x] Validate tool structured output when `outputSchema` is declared.
- [x] Add capability-honesty tests so advertised capabilities have registered handlers.
- [x] Keep list-change capabilities false/omitted until notification emitters exist.
