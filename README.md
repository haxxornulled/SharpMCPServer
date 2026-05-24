# MCPServer

C#/.NET 10 Model Context Protocol server targeting MCP `2025-11-25`.

The current implementation is the Phase 1 stdio foundation: JSON-RPC message handling, strict stdio transport, lifecycle negotiation, request gating, ping, cancellation notification handling, logging level support, tools, resources, resource subscriptions, prompts, completion, JSON Schema validation, and transcript-style protocol coverage.

## Build

```powershell
dotnet restore .\MCPServer.slnx
dotnet build .\MCPServer.slnx -c Release --no-restore
dotnet test .\MCPServer.slnx -c Release --no-build
```

Or run the repository verification gate:

```powershell
.\scripts\verify-mcp-phase1.ps1
```

## Run as stdio MCP server

```powershell
dotnet run --project .\MCPServer.Host\MCPServer.Host.csproj -c Release --no-build
```

Paste newline-delimited JSON-RPC frames on stdin:

```jsonl
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"manual-smoke","version":"1"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"ping"}
{"jsonrpc":"2.0","id":3,"method":"tools/list"}
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"server.info","arguments":{}}}
```

## Hard rules

- Built-in `System.Text.Json` only. No Newtonsoft.
- Autofac modules and `AutofacServiceProviderFactory` for composition.
- No MediatR.
- Recoverable protocol/application failures use `LanguageExt.Fin<T>` with `Fin.Succ<T>` / `Fin.Fail<T>`.
- Do not use `using static LanguageExt.Prelude` in protocol code.
- stdout is reserved for newline-delimited MCP JSON-RPC frames under stdio. Logs must go to stderr.
- Hot-path buffers should use pooling where practical and lifetime-safe.
- stdio input frames must be newline-delimited UTF-8. CRLF is tolerated; embedded raw line breaks are rejected.
- Capability honesty is mandatory: only advertise a server capability when its handlers and acceptance tests exist.

## Project layout

```text
MCPServer.Domain          Protocol constants and DTOs
MCPServer.Application     Dispatch, lifecycle, registries, handlers, application ports
MCPServer.Infrastructure  JSON-RPC serialization, schema validation, stdio transport adapters
MCPServer.Host            Generic Host entry point
MCPServer.UnitTests       Unit-level parser/serializer/dispatcher/registry tests
MCPServer.ProtocolTests   Transcript-style protocol and stdio frame tests
docs                      Compliance matrix, architecture notes, acceptance gates, troubleshooting
scripts                   Local verification gates
```

## Phase 1 closeout docs

- `docs/MCP_2025_11_25_COMPLIANCE_MATRIX.md`
- `docs/PHASE1_ACCEPTANCE_CHECKLIST.md`
- `docs/PHASE1_PROTOCOL_HARNESS.md`
- `docs/DESIGN_ARCHITECTURE_SCORECARD.md`
- `docs/STDIO_TROUBLESHOOTING.md`
- `docs/STDIO_TRANSPORT_SPEC_DRILLDOWN.md`

## Built-in resources

The Phase 1 server exposes a minimal MCP resources surface:

```json
{"jsonrpc":"2.0","id":10,"method":"resources/list"}
```

The built-in static resource is:

```text
mcpserver://server/info
```

Read it with:

```json
{"jsonrpc":"2.0","id":11,"method":"resources/read","params":{"uri":"mcpserver://server/info"}}
```

`resources/templates/list` is implemented and currently returns an empty `resourceTemplates` array. Resource subscriptions are advertised and implemented for known static resources through `resources/subscribe` and `resources/unsubscribe`; resource list-change notifications are intentionally not advertised because Phase 1 does not have a dynamic resource registry.

## Optional SSH Tool Pack

This package includes an optional `MCPServer.Tools.Ssh` project that registers real MCP tools when enabled:

- `ssh.profiles.list`
- `ssh.exec`
- `ssh.agent.launch`
- `ssh.agent.status`
- `ssh.agent.output`
- `ssh.agent.cancel`

The SSH tool surface is registered at startup so MCP clients can discover it consistently. Runtime execution is controlled by `McpTools:Ssh:Enabled` through `IOptionsMonitor<SshToolSettings>`; when disabled, SSH calls return a normal tool-level policy denial. This supports future Visual Studio extension management without bouncing the MCP server process.

Full SSH documentation:

- `docs/SSH_USAGE_GUIDE.md` — end-to-end usage guide.
- `docs/SSH_TOOLS.md` — server-side MCP tool-pack behavior.
- `docs/SSH_HOST_SIDECAR.md` — sidecar CLI, vault, profiles, `serve`, and `run`.
- `docs/SSH_AGENTS.md` — background SSH agent launch/status/output/cancel flow.
- `docs/LM_STUDIO_SSH_TOOLS.md` — LM Studio setup and troubleshooting.
- `docs/AGENT_ROUTER_DESIGN.md` — proposed host-side Agent Router boundary.

## Client/host sample

The solution includes a host-side MCP client sample:

```text
MCPServer.Client
MCPServer.Client.Console
```

`MCPServer.Client` starts one server process over stdio and maintains one stateful MCP session with it. `MCPServer.Client.Console` demonstrates the client flow: `initialize`, `notifications/initialized`, `tools/list`, then `tools/call`. See `docs/CLIENT_ARCHITECTURE.md`.

## SSH host sidecar

`MCPServer.Host.Sidecar` is an optional host/client-side companion for SSH profile and credential management. It owns the local SSH vault, writes the SSH profile file consumed by `MCPServer.Tools.Ssh`, and starts `MCPServer.Host` with only the referenced vault secrets hydrated as environment variables.

Normal developer setup uses:

```powershell
MCPServer.Host.Sidecar.exe ssh add-password <profile> --host <host> --username <user> [--replace true] [options]
MCPServer.Host.Sidecar.exe profile replace <profile> --host <host> --username <user> [options]
```

The sidecar also includes low-level `vault` and `profile` commands for automation. Password auth is the current supported workflow; private-key plumbing exists but is not the documented happy path. The sidecar is not itself advertised as an MCP tool.

## SSH profiles, vault, and root override

Profiles define host, username, credential source, host-key pin, allowed commands, denied commands, allowed remote path prefixes, and privilege flags. Secrets should be stored in the sidecar vault and referenced through generated `MCPSERVER_SSH_VAULT_*` environment variable names.

`allowedRoot` is an explicit root SSH override. Root-capable profiles require `username=root`, `allowedRoot=true`, `privileged=true`, a pinned `hostKeySha256`, and unknown host keys disabled. The sidecar refuses to create/update such profiles unless `--i-understand-root-ai-risk true` is supplied.

## SSH agents

The SSH tool pack exposes background, pollable SSH command sequences through normal MCP tools:

```text
ssh.agent.launch  -> starts a run and returns agentId
ssh.agent.status  -> returns step state and output tails
ssh.agent.output  -> returns incremental stdout/stderr by offset
ssh.agent.cancel  -> requests cancellation
```

This is intentionally implemented as ordinary MCP tools for current client compatibility. The server does not yet advertise the experimental MCP `tasks` capability. Native task support should be added only after `tasks/get`, `tasks/result`, `tasks/list`, and `tasks/cancel` are implemented and tested.

## LM Studio SSH tool exposure

If LM Studio only shows `server.info`, the SSH tool module is not enabled in the process LM Studio launched.

Preferred vault-backed launch command:

```powershell
MCPServer.Host.Sidecar.exe serve --server-path "C:\Visual Studio Projects\MCPServer\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe"
```

Do not configure LM Studio to use the sidecar `run` command; `run` is a human diagnostics command, while `serve` is the protocol-clean stdio proxy.

Direct host launch is also valid when the parent process supplies:

```text
McpTools__Ssh__Enabled=true
McpTools__Ssh__ProfilePath=<profile file>
```

and any referenced `MCPSERVER_SSH_VAULT_*` variables.

## Agent Router direction

The next clean step is a host-side Agent Router, not a larger SSH tool pack. The router should own objective planning, consent, model context, and multiple MCP client sessions. The SSH tool pack should remain the execution backend for SSH commands and background SSH agent runs. See `docs/AGENT_ROUTER_DESIGN.md`.


## SSH profile and vault file locations

The canonical user-level SSH sidecar location is `%LOCALAPPDATA%\McpServer\ssh` on Windows. The sidecar writes profiles to `%LOCALAPPDATA%\McpServer\ssh\ssh-profiles.local.json`, vault metadata to `%LOCALAPPDATA%\McpServer\ssh\ssh-vault.local.json`, and the local vault key to `%LOCALAPPDATA%\McpServer\ssh\ssh-vault.key`. The MCP server reads that same LocalAppData profile file by default. A legacy Roaming AppData profile path is read only as a temporary compatibility fallback.
