# Agent Router Design Notes

This document captures the intended direction for an Agent Router without violating the MCP host/client/server boundary.

## Current status

The current implementation has two different concepts that should stay separate:

```text
Host sidecar
  local developer/admin executable
  manages SSH vault entries
  manages SSH profiles
  launches MCPServer.Host with hydrated env vars
  not advertised as MCP tools

SSH agent tools
  server-side MCP tools
  advertised through tools/list when SSH is enabled
  launch/poll/output/cancel background SSH command sequences
```

The SSH agent tool model is intentionally simple and works today in clients such as LM Studio.

## Where an Agent Router belongs

The preferred first implementation is **host-side**:

```text
AgentRouter.Host
  owns model prompts and high-level objectives
  owns authorization and consent UX
  owns multiple MCP client sessions
  can launch MCPServer.Host through the sidecar serve path
  can connect to additional MCP servers later
  decides which server/tool/agent should handle each objective
  records run state, transcript summaries, and operator decisions
```

That aligns with MCP's architecture: the host creates and manages clients, enforces security/consent, coordinates model integration, and aggregates context. Servers remain focused and isolated.

## Why not put the router directly in MCPServer.Host?

A server-side router is possible, but it is a heavier pattern: an MCP server that also acts as a client to downstream MCP servers. That can be useful later for deterministic workflows, but it has sharp edges:

- it can blur security boundaries between servers;
- it can accidentally expose context from one downstream server to another;
- it can cause nested tool-call loops that are hard for the host UI to reason about;
- it can make the server too broad instead of focused;
- it can hide meaningful operator consent decisions behind one large tool call.

The clean approach is host-side first, mediator/server-side later only when there is a specific reason.

## Recommended v1 Agent Router shape

Add a separate project later, not inside `MCPServer.Tools.Ssh`:

```text
MCPServer.AgentRouter.Abstractions
MCPServer.AgentRouter.Host
MCPServer.AgentRouter.Console   optional diagnostics CLI
```

Core abstractions:

```csharp
public interface IAgentRouter
{
    ValueTask<Fin<AgentRunStarted>> LaunchAsync(AgentObjective objective, CancellationToken cancellationToken);
    ValueTask<Fin<AgentRunStatus>> GetStatusAsync(string runId, CancellationToken cancellationToken);
    ValueTask<Fin<AgentRunOutput>> GetOutputAsync(AgentOutputCursor cursor, CancellationToken cancellationToken);
    ValueTask<Fin<AgentCancelResult>> CancelAsync(string runId, CancellationToken cancellationToken);
}
```

The router should be async-first and use `ValueTask` for hot-path abstractions that may complete synchronously.

## Suggested router responsibilities

A host-side router can do things the MCP server should not do:

- decide whether an objective needs SSH, file tools, git tools, HTTP tools, or multiple servers;
- ask the user for confirmation before dangerous actions;
- maintain operator-visible run state;
- coordinate multiple MCP client sessions;
- preserve host-level conversation and model context;
- apply per-profile, per-server, and per-user policies;
- translate objectives into concrete tool calls;
- retry safe steps and stop on unsafe failures;
- keep long-running status/output UI separate from the server protocol core.

## Suggested router run model

```text
queued
planning
awaiting_approval
working
input_required
completed
failed
cancelled
```

The router should persist a compact run record:

```json
{
  "runId": "agent-run-...",
  "objective": "Configure nginx on Debian 13",
  "status": "working",
  "server": "mcpserver-host",
  "tool": "ssh.agent.launch",
  "relatedAgentId": "ssh-agent-...",
  "createdAt": "...",
  "lastUpdatedAt": "..."
}
```

## Router-to-SSH flow

For the current implementation, the router should call the existing SSH agent tools:

```text
1. tools/list
2. verify ssh.agent.launch exists
3. tools/call ssh.profiles.list
4. select profile or request user/profile selection
5. tools/call ssh.agent.launch
6. poll ssh.agent.status
7. tail ssh.agent.output
8. call ssh.agent.cancel if operator cancels
```

This keeps compatibility with LM Studio and other clients that only understand ordinary MCP tools.

## Future MCP native task support

MCP `2025-11-25` introduced experimental task-augmented requests. Do not advertise task support until all required pieces exist.

A native task implementation would require:

```text
capabilities.tasks.list
capabilities.tasks.cancel
capabilities.tasks.requests.tools.call

tasks/get
tasks/result
tasks/list
tasks/cancel
optional notifications/tasks/status
```

Tool descriptors can then use `execution.taskSupport` for task-aware tools, but only after the server advertises compatible task capabilities.

Recommended migration path:

1. Keep current `ssh.agent.*` tools as stable compatibility APIs.
2. Add generic server task registry and task DTOs.
3. Add JSON-RPC handlers for `tasks/get`, `tasks/result`, `tasks/list`, and `tasks/cancel`.
4. Advertise `capabilities.tasks` only when tests prove every task handler works.
5. Mark selected long-running tools with `execution.taskSupport: "optional"`.
6. Continue supporting `ssh.agent.*` because many MCP clients will lag native task support.

## Router tool surface if implemented as an MCP server later

If a future server-side router is required, expose it as a separate MCP server/tool pack rather than bloating the SSH tool pack:

```text
agent.router.launch
agent.router.status
agent.router.output
agent.router.cancel
agent.router.runs.list
```

Each router tool result should include structured content and text fallback. It should not expose downstream secrets, raw vault entries, or full host conversation context.

## Security gates

Router approval should be stricter than raw SSH tool policy for high-risk profiles:

- root profile selected;
- `allowedRoot=true`;
- `allowAllCommands=true`;
- shell interpreter command such as `bash -lc`;
- package install/remove;
- service restart/reload;
- firewall/network changes;
- identity/key/user changes;
- destructive file operations;
- recursive ownership/permission changes.

For these, the router should require explicit operator confirmation, not just model confidence.

## Design rule

The router may coordinate agents. The SSH tool pack executes SSH. The sidecar manages secrets/profiles. The host owns consent and lifecycle. Keep those boundaries intact.
