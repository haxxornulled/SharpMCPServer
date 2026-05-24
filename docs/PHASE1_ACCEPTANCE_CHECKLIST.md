# Phase 1 Acceptance Checklist

Run this checklist before declaring Phase 1 complete.

## Build

```powershell
dotnet restore .\MCPServer.slnx
dotnet build .\MCPServer.slnx -c Release --no-restore
dotnet test .\MCPServer.slnx -c Release --no-build
```

## Manual stdio transcript

Start the server:

```powershell
dotnet run --project .\MCPServer.Host\MCPServer.Host.csproj -c Release --no-build
```

Paste these JSONL frames, one per line:

```jsonl
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"manual-smoke","version":"1"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"ping"}
{"jsonrpc":"2.0","id":3,"method":"tools/list"}
{"jsonrpc":"2.0","id":4,"method":"logging/setLevel","params":{"level":"warning"}}
{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"server.info","arguments":{}}}
```

Expected behavior:

- `initialize` returns `protocolVersion`, `capabilities`, and `serverInfo`.
- `notifications/initialized` returns no response.
- `ping` returns `{}`.
- `tools/list` includes `server.info`.
- `logging/setLevel` returns `{}`.
- `tools/call server.info` returns text content with `isError=false`.

## Negative-path checks

Paste these after initialization:

```jsonl
{"jsonrpc":"2.0","id":10,"method":"no/suchMethod"}
{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"server.info","arguments":{"unexpected":true}}}
{"jsonrpc":"2.0","id":12,"method":"logging/setLevel","params":{"level":"verbose"}}
{"jsonrpc":"2.0","id":13,"method":"tools/list","params":[]}
{"jsonrpc":"2.0","id":13,"method":"ping"}
{"jsonrpc":"2.0","id":14,"method":"notifications/cancelled","params":{"requestId":2}}
```

Expected behavior:

- Unknown request method returns JSON-RPC `method not found`.
- Invalid tool arguments return a successful JSON-RPC response containing an MCP tool result with `isError=true`.
- Invalid logging level returns JSON-RPC `invalid params`.
- Non-object `params` returns JSON-RPC `invalid request`.
- Reused request ID returns JSON-RPC `invalid request`.
- MCP notification method with an `id` returns JSON-RPC `invalid request` and must not mutate lifecycle state.

## stdout/stderr rule

When running under stdio, stdout must contain only JSON-RPC frames. Logs and diagnostics must go to stderr. This remains a hard rule for every future phase.


## stdio transport strictness

- Input frames must be UTF-8 encoded.
- Input frames must be terminated by `\n`.
- CRLF is tolerated and normalized.
- Raw embedded carriage returns are rejected.
- EOF with a partial unterminated frame is rejected by default.
- Output frames must contain exactly one trailing `\n` delimiter and no embedded raw `\r` or `\n`.
- `Console.WriteLine`, startup banners, and diagnostic dumps are forbidden in stdio mode.

Guard command:

```bash
grep -R "Console\.\|WriteLine" -n MCPServer.* --exclude-dir=bin --exclude-dir=obj
```

The only allowed result is `Console.OpenStandardInput()` / `Console.OpenStandardOutput()` in the stdio transport session.


## Spec Tightening Update

- Inbound JSON-RPC response messages are now recognized and ignored; this server does not emit responses to responses.
- `initialize` now validates required fields before session state is marked initialized.
- `ping` now rejects supplied params because the MCP ping request is defined with no parameters.
- Tool results with `structuredContent` now place the serialized JSON into a text content block for backward compatibility.

- [x] Malformed parse errors omit `id` when no valid MCP request ID can be read.

## Additional green gates added in metadata/icon pass

- Invalid request `_meta` key returns JSON-RPC Invalid params.
- Valid request `_meta.progressToken` remains accepted.
- `tools/call.arguments` rejects non-object JSON values as Invalid params.
- Tool descriptors reject unsafe icon source schemes.
- Tool descriptors allow valid HTTPS icon metadata.

- [x] Opaque pagination cursors are emitted for paged `tools/list` responses, and guessed/invalid cursor values return Invalid params.

## Resources smoke acceptance

- [x] `initialize` response declares `capabilities.resources`.
- [x] `resources/list` returns at least `mcpserver://server/info`.
- [x] `resources/read` for `mcpserver://server/info` returns `contents` with `application/json` text.
- [x] `resources/templates/list` returns a valid empty list result.
- [x] `resources/list` rejects invalid guessed cursors.


## Prompts Update

- Implemented `prompts` capability.
- Implemented `prompts/list` with opaque cursors.
- Implemented `prompts/get` with string-only argument validation.
- Added built-in `server.status` prompt.

- [x] Completion utility advertises `completions` capability and handles `completion/complete` for prompt refs.
- [x] Completion results enforce the 100-value MCP limit.

## Resource subscription update

Implemented `resources/subscribe` and `resources/unsubscribe`; `initialize` now advertises `resources.subscribe: true`. Unknown subscription targets map to `-32002` Resource not found. Update notifications are modeled but not emitted by static Phase 1 resources.


## Client capability / roots foundation update

- Captures client capability declarations from `initialize` into session state.
- Validates known client capability entries (`roots`, `sampling`, `elicitation`, `tasks`) are object-shaped when supplied.
- Tracks `roots.listChanged` and accepts `notifications/roots/list_changed` without a response, incrementing the roots revision so future roots caches can invalidate cleanly.
- Does not advertise roots as a server capability; roots are a client feature used only when a future tool/resource needs client-provided workspace boundaries.

## Added acceptance checks: schema and capability honesty

- [ ] Visual Studio Test Explorer is green after `JsonSchema.Net` restore.
- [x] Tool schemas are validated using a JSON Schema implementation, not only the local subset validator.
- [x] `inputSchema` and `outputSchema` are validated at tool registry construction.
- [x] Tool call arguments are validated before tool execution.
- [x] Tool `structuredContent` is validated when `outputSchema` exists.
- [x] `initialize` advertised server capabilities are backed by registered method handlers.
- [x] `tools.listChanged`, `prompts.listChanged`, and `resources.listChanged` are not advertised as true until dynamic list-change notification emitters exist.
