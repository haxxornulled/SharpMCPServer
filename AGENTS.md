# AGENTS.md

Repository guidance for agentic changes in `MCPServer`.

## Architecture

- Keep the system explicit, layered, and enterprise-grade.
- Prefer constructor injection and explicit service interfaces.
- Keep protocol, application, infrastructure, and host concerns separate.
- Use explicit DTO and domain translations. Do not introduce AutoMapper.
- Prefer `LanguageExt.Fin<T>` for recoverable operation results.
- Use xUnit for tests.
- Do not introduce MediatR.
- Do not introduce repository pattern unless a task explicitly asks for it.
- Do not hide control flow behind magic abstractions or giant switchboards.
- Keep classes small and focused.
- For Python or other non-.NET interop, keep the bridge in a dedicated NativeAOT shared library with a tiny C ABI, UTF-8 JSON in/out, explicit unmanaged ownership, and source-generated `System.Text.Json` contracts. Do not expose C# object graphs, Autofac, or host composition details across that boundary.
- The Python consumer package for a NativeAOT bridge should be standard-library-first, use `ctypes` around the C ABI, resolve the native library explicitly or from a conventional package-local path, and stay free of `pythonnet` or CLR bootstrap tricks.

## Security

- Treat approval and policy boundaries as first-class.
- When working on MCP client or transport code, follow the 2025-11-25 spec literally: do not send follow-up traffic before initialize, include the negotiated `MCP-Protocol-Version` on subsequent HTTP requests, and mirror required request headers exactly.
- For Streamable HTTP, bind only to loopback by default, validate `Origin` before processing, and reject any `Mcp-Method`, `Mcp-Name`, or `Mcp-Param-*` header that does not match the JSON-RPC body.
- For Streamable HTTP bidirectional streaming, do not open server-to-client request paths until initialization has completed and the client has sent `notifications/initialized`; keep client feature requests gated until the session is explicitly ready.
- For Streamable HTTP GET streams, require a valid `MCP-Session-Id`, require `MCP-Protocol-Version`, honor `Last-Event-ID` only for replay within the same session, and terminate cleanly on `DELETE`.
- SSE event IDs must stay globally unique within a session, and replay must never leak messages across sessions or streams.
- Keep Streamable HTTP replay history bounded per session. Explicit `Last-Event-ID` cursors older than the retained window must be rejected instead of indexing into pruned history.
- For Streamable HTTP authorization, fail closed. Public requests must not touch protected MCP handlers until bearer validation has passed, `401` must advertise `resource_metadata`, `403` must advertise `insufficient_scope`, and the protected-resource metadata document must stay public and spec-shaped.
- Protect the MCP HTTP boundary with explicit bearer validation and OIDC discovery. Prefer `IHttpClientFactory` for discovery fetches and keep auth metadata, scope checks, and token validation in Infrastructure.
- MCP clients must ignore off-origin `resource_metadata` hints from `WWW-Authenticate` challenges. Only same-origin challenge metadata is eligible; otherwise fall back to the well-known protected-resource URI on the MCP endpoint origin.
- MCP authorization-server discovery must support both OAuth 2.0 Authorization Server Metadata and OpenID Connect Discovery 1.0. Reject metadata that does not advertise `code_challenge_methods_supported` with `S256`, because MCP requires PKCE-capable auth servers.
- Interactive OAuth authorization-code clients must use PKCE, loopback redirect handling on localhost, and explicit `resource` parameters on both authorization and token requests. The browser/loopback/token-exchange implementation belongs in client infrastructure or the composition root, not in `MCPServer.Client`. Support pre-registered client IDs, client ID metadata documents, and dynamic client registration in that order of preference, but never treat an access token as reusable after a server challenge has already rejected it.
- The interactive OAuth authorization provider should be a singleton within its composition root so token cache, refresh state, and loopback flow state remain coherent for the life of the client process.
- Do not log authorization codes, access tokens, refresh tokens, PKCE verifiers, bearer headers, or raw OAuth metadata payloads. Keep any browser-launch, loopback, registration, and token-exchange state tightly scoped to the auth flow lifetime and dispose it deterministically.
- For SQLite-backed SSH profile and credential stores, keep connections pooled, set WAL mode and a busy timeout, initialize each schema once per database path, and avoid coarse per-operation serialization. Only the narrowest bootstrap path may use an explicit lock, such as first-time master-key creation.
- For high-concurrency HTTP clients, prefer named `IHttpClientFactory` registrations with explicit handler limits and lifetime settings at the composition boundary instead of relying on the defaults.
- For stdio shutdown, close the child input stream first, wait for the server to exit, then escalate to a process kill only after a short grace period.
- Prefer `IHttpClientFactory` for outgoing MCP HTTP clients at composition boundaries. Keep the factory scope alive for the session lifetime, dispose the resulting client through the session, and use explicit `HttpMessageHandler` injection only for tests or narrow transport seams.
- No tool execution, remote API call, MCP server action, SSH action, or background agent run may happen until policy has produced an explicit approval decision or token gate.
- If a request needs approval, keep it read-only until the approval token or equivalent policy signal is present.
- Do not log approval tokens, auth headers, secrets, or raw credential material.
- Do not leak control-plane metadata into model prompts.
- Strip internal routing, approval, and session metadata from prompt/context payloads before handing them to a model.
- Keep SSH concerns limited to SSH execution backends. Do not expand the SSH tool pack to solve Agent Router concerns.
- Do not silently mutate files outside configured workspace roots.
- Do not add shell execution unless it is explicitly policy-gated.
## Cancellation

- Every async boundary should accept `CancellationToken`.
- Cancellation must be propagated, not converted into generic failure.
- Catch `OperationCanceledException` only when you are intentionally translating or rethrowing cancellation at a boundary.
- Do not swallow cancellation in background services, router decisions, model calls, or tool execution paths.

## Workflow

- Prefer deterministic behavior over cleverness.
- Add tests for important behavior and security boundaries.
- When making changes, keep the implementation minimal but production-shaped.
- When in doubt, preserve explicitness and make the boundary obvious in code.
- For the NativeAOT Python bridge, export only boring C ABI entry points, require Python to free returned buffers through the matching export, and keep shutdown/lifecycle hooks explicit even if they are no-ops initially.
- The Python bridge release flow should publish the NativeAOT library, sync the package-local native payload, build a platform wheel from the checked-in Python package, and validate that wheel from a clean working directory so we are testing the installed artifact, not just the source tree.
- Keep the published native bridge copyable into the Python package-local `native/` directory through the checked-in sync script, and keep that directory gitignored except for sentinel files.
- Keep protocol-facing documentation aligned with `docs/SPEC_COMPLIANCE.md` and the current stable MCP revision; do not let README or boundary docs drift ahead of the actual implementation.
