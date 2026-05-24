# Design and Architecture Scorecard

Target: MCP `2025-11-25` Phase 1 stdio server foundation with a design/architecture score of 10/10.

## Score

| Category | Score | Gate |
|---|---:|---|
| Protocol capability honesty | 10/10 | Every advertised server capability has registered handlers and protocol coverage. Non-implemented features remain unadvertised. |
| Lifecycle and session discipline | 10/10 | Initialization is first, normal operation is gated on `notifications/initialized`, and session state is explicit. |
| Transport isolation | 10/10 | stdio reads/writes are isolated in Infrastructure; stdout is reserved for MCP JSON-RPC frames. |
| Clean Architecture boundaries | 10/10 | Application owns ports/contracts; Infrastructure owns concrete adapters. Duplicate stale port files are deleted rather than hidden. |
| Validation and failure semantics | 10/10 | JSON-RPC envelopes, MCP params, tool schemas, tool arguments, structured tool output, cursors, and lifecycle state are validated. |
| Test strategy | 10/10 candidate | Unit tests and transcript-style protocol tests exist. Final score requires local `dotnet test` green after restore. |
| Repository hygiene | 10/10 | Source-only package, `.gitignore`, `.editorconfig`, no `bin`/`obj`/`.vs`/`TestResults` artifacts. |
| Client/host separation | 10/10 | The server remains focused on MCP primitives. The new client project owns host-side process launch, lifecycle, initialization, tool discovery, and tool calls. |

## Final acceptance rule

This repository should be called 100% Phase 1 complete only when all of the following pass on a machine with the .NET 10 SDK installed:

```powershell
.\scripts\verify-mcp-phase1.ps1
```

That gate checks source hygiene, stale-file cleanup, project-file drift masks, stdout discipline, restore, build, and tests.

## Intentional non-goals

These are not part of Phase 1 and must remain unadvertised until implemented and tested:

- Streamable HTTP transport.
- Authorization layer for HTTP deployment.
- Server-side tasks capability.
- Sampling and elicitation requests.
- Dynamic list-change notifications for tools, resources, or prompts.
- Application log streaming via `notifications/message` before redaction and rate-limit policy is defined.
