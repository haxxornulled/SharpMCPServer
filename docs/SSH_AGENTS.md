# SSH Agents

`MCPServer.Tools.Ssh` exposes agent-oriented MCP tools for long-running SSH operations.

These tools are advertised by the MCP host at startup. Runtime execution is still gated by `McpTools:Ssh:Enabled` and the SSH policy. The host sidecar CLI is not an MCP tool; it is a developer/admin utility for configuring vault items and profiles.

## Tools

| Tool | Purpose |
| --- | --- |
| `ssh.agent.launch` | Starts a background SSH agent run with a policy-checked command sequence. |
| `ssh.agent.status` | Returns status, current step, step summaries, and stdout/stderr tails. |
| `ssh.agent.output` | Reads incremental stdout/stderr using offsets. |
| `ssh.agent.cancel` | Requests cancellation of a running agent. |

The direct command tool remains available as `ssh.exec` for one-off execution.

## Launch request shape

```json
{
  "profile": "dev",
  "objective": "Describe the operation for logs and operator UX",
  "workingDirectory": "/home/james/work",
  "timeoutSecondsPerStep": 300,
  "operationKey": "optional-stable-correlation-id",
  "commands": [
    {
      "command": "whoami",
      "arguments": [],
      "workingDirectory": null,
      "timeoutSeconds": null
    }
  ]
}
```

Each step is executed through the same `ISshExecutionService` and policy path as `ssh.exec`.

## Example: configure nginx on Debian 13

The model or host-side router should generate a concrete command sequence. The server does not synthesize shell scripts by itself; it executes what the tool caller provides after profile policy evaluation.

```json
{
  "profile": "debian-root-lab",
  "objective": "Install and configure nginx on Debian 13",
  "workingDirectory": "/root",
  "timeoutSecondsPerStep": 300,
  "commands": [
    { "command": "apt-get", "arguments": ["update"] },
    { "command": "apt-get", "arguments": ["install", "-y", "nginx"] },
    { "command": "systemctl", "arguments": ["enable", "--now", "nginx"] },
    { "command": "nginx", "arguments": ["-t"] },
    { "command": "systemctl", "arguments": ["status", "nginx", "--no-pager"] }
  ]
}
```

For intentionally root-level shell workflows, a profile can use `username: root`, `allowedRoot: true`, `privileged: true`, `allowAllCommands: true`, and a pinned `hostKeySha256`. In that mode the SSH execution policy treats `allowedRoot` as the operator's explicit override, including inline shell interpreter commands such as `bash -lc`, while still requiring host-key pinning.

## Polling status

After launch, call `ssh.agent.status`:

```json
{ "agentId": "ssh-agent-..." }
```

The response includes:

```text
agentId
status
profile
objective
commandCount
currentStep
completedSteps
failedSteps
currentCommand
summary
stdoutTail
stderrTail
stdoutLength
stderrLength
steps
createdAt
lastUpdatedAt
completedAt
```

Statuses are:

```text
queued
working
completed
failed
cancelled
```

## Reading terminal output

Call `ssh.agent.output` with offsets:

```json
{
  "agentId": "ssh-agent-...",
  "stdoutOffset": 0,
  "stderrOffset": 0,
  "maxChars": 20000
}
```

The response includes:

```text
stdout
stderr
stdoutOffset
stderrOffset
nextStdoutOffset
nextStderrOffset
stdoutTruncated
stderrTruncated
```

Use `nextStdoutOffset` and `nextStderrOffset` on the next call. This is the current terminal-output streaming pattern for stdio clients that do not support MCP native tasks yet.

## Cancelling an agent

```json
{ "agentId": "ssh-agent-..." }
```

Cancellation is cooperative. If SSH.NET is already inside a blocking remote command, the runtime marks the agent cancelled and requests cancellation, but the remote process may keep running until SSH.NET returns or the command timeout expires.

## Current model vs MCP native tasks

MCP `2025-11-25` includes experimental task-augmented requests. This implementation intentionally does not advertise the MCP `tasks` capability yet.

The current model is ordinary tools:

```text
ssh.agent.launch  -> create background run
ssh.agent.status  -> poll state
ssh.agent.output  -> read output windows
ssh.agent.cancel  -> request cancellation
```

Native MCP tasks require `capabilities.tasks` plus handlers such as `tasks/get`, `tasks/result`, `tasks/list`, and `tasks/cancel`. Once those exist, the agent launch path can be mapped to task-augmented `tools/call`, but the current tool-based model should remain for compatibility with clients that only support `tools/list` and `tools/call`.

## Router integration

A host-side Agent Router should treat these tools as the SSH execution backend:

```text
router objective -> choose profile -> call ssh.agent.launch -> poll status/output -> report to user
```

The router should not bypass profile policy, vault boundaries, or root consent gates. See `docs/AGENT_ROUTER_DESIGN.md`.
