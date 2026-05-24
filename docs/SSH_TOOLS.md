# SSH Tools Project

`MCPServer.Tools.Ssh` is an MCP tool-pack project. It registers real MCP tools through the core `IMcpTool` contract and Autofac composition, but the core protocol projects do not depend on the SSH project.

See `docs/SSH_USAGE_GUIDE.md` for the operator quickstart.

## Advertised tools

The host registers these tools at startup so MCP clients can discover the SSH surface consistently. Runtime execution is controlled by `McpTools:Ssh:Enabled` through `IOptionsMonitor<SshToolSettings>`:

| Tool | Purpose |
| --- | --- |
| `ssh.profiles.list` | Lists configured SSH profiles without exposing credentials. |
| `ssh.exec` | Executes one policy-approved SSH command through a configured profile. |
| `ssh.agent.launch` | Starts a background SSH command sequence and returns an `agentId`. |
| `ssh.agent.status` | Returns status, step state, and output tails for a launched agent. |
| `ssh.agent.output` | Reads incremental stdout/stderr from a launched agent. |
| `ssh.agent.cancel` | Requests cancellation of a launched agent. |

When SSH execution is disabled, calls return a normal tool-level policy denial. This keeps the tool surface stable while still allowing appsettings changes from tools such as a future Visual Studio extension to affect runtime policy without restarting the MCP server.

## Configuration

Default host configuration keeps SSH execution disabled:

```json
{
  "McpTools": {
    "Ssh": {
      "Enabled": false
    }
  }
}
```

Enable it through appsettings, user secrets, environment variables, or the sidecar `serve` command:

```json
{
  "McpTools": {
    "Ssh": {
      "Enabled": true,
      "ProfilePath": "config/mcpserver/ssh-profiles.local.json",
      "TimeoutSeconds": 60,
      "MaxOutputChars": 20000,
      "RequireExplicitProfileAllowlist": true,
      "AllowUnknownHostKeys": false,
      "AllowShellInterpreterInlineCommands": false,
      "AllowedCommands": ["cat", "dotnet", "echo", "git", "ls", "pwd", "uname", "whoami"],
      "DeniedCommands": ["chmod", "chown", "dd", "fdisk", "mkfs", "mount", "nc", "netcat", "passwd", "reboot", "rm", "shutdown", "su", "sudo", "umount", "useradd", "userdel", "usermod"]
    }
  }
}
```

Profile files are loaded from:

1. `McpTools:Ssh:ProfilePath`, when configured.
2. `config/mcpserver/ssh-profiles.local.json` under the host content root.
3. The user-local sidecar path, normally `%LOCALAPPDATA%/McpServer/ssh/ssh-profiles.local.json`.

The host content root is set to `AppContext.BaseDirectory`, so config files beside `MCPServer.Host.exe` load correctly even when launched by GUI clients from another working directory.

## Runtime options reload

`MCPServer.Host` binds this section with `IOptionsMonitor<SshToolSettings>`. The execution policy, profile-store path lookup, and trace writer read `CurrentValue` at use time. This allows runtime changes to `Enabled`, allowlists, denied commands, profile path, trace path, timeout, and output limits without restarting the host process.

## Recommended setup path

Use the password-backed sidecar workflow for developer/operator setup:

```powershell
MCPServer.Host.Sidecar.exe ssh add-password dev --host 192.168.1.50 --username james --host-key-sha256 SHA256:REPLACE_WITH_PIN --allowed-command whoami
```

Password auth is the current supported happy path. Private-key CLI plumbing exists for later hardening, but this guide intentionally avoids keypair setup.

Then configure MCP clients to launch:

```powershell
MCPServer.Host.Sidecar.exe serve --server-path "C:\Visual Studio Projects\MCPServer\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe"
```

## Security posture

SSH execution is intentionally profile-driven:

- Tool callers cannot provide passwords, private keys, passphrases, hosts, ports, or usernames.
- Credentials must come from profile configuration and vault-hydrated environment-variable names.
- Credentials should be managed through the host sidecar vault.
- Commands are executable names only in normal profiles.
- Arguments must be passed as an array and are POSIX-quoted before remote execution.
- `bash -c`, `sh -c`, `pwsh -c`, `cmd /c`, and similar inline shell patterns are denied by default.
- Unknown host keys are denied by default.
- `allowAllCommands` requires the profile to be marked `privileged` and to pin a host key.
- Root SSH profiles are denied unless `allowedRoot=true`, `privileged=true`, and a pinned host key is configured.
- Execution traces are written to a local trace directory for auditability.

## Root override behavior

`allowedRoot` is the explicit operator override for root SSH profiles. It does **not** silently make normal profiles dangerous.

Root-capable execution requires:

```text
username = root
allowedRoot = true
privileged = true
hostKeySha256 is pinned
acceptUnknownHostKey = false
```

For unrestricted root command execution, also require:

```text
allowAllCommands = true
```

In that mode, inline shell interpreter commands are permitted because the operator intentionally configured the danger path. The sidecar still requires `--i-understand-root-ai-risk true` when creating or updating such profiles.

## One-off execution example

```json
{
  "name": "ssh.exec",
  "arguments": {
    "profile": "dev",
    "command": "git",
    "arguments": ["status", "--short"],
    "workingDirectory": "/home/james/work",
    "timeoutSeconds": 30,
    "operationKey": "manual-check-001"
  }
}
```

## Background SSH agent example

```json
{
  "name": "ssh.agent.launch",
  "arguments": {
    "profile": "debian-root-lab",
    "objective": "Install and configure nginx on Debian 13",
    "workingDirectory": "/root",
    "timeoutSecondsPerStep": 300,
    "commands": [
      { "command": "apt-get", "arguments": ["update"] },
      { "command": "apt-get", "arguments": ["install", "-y", "nginx"] },
      { "command": "systemctl", "arguments": ["enable", "--now", "nginx"] },
      { "command": "nginx", "arguments": ["-t"] }
    ]
  }
}
```

Then use `ssh.agent.status`, `ssh.agent.output`, and `ssh.agent.cancel` with the returned `agentId`.

## Design notes

The original SSH prototype had good primitives but was coupled to `AgentRouter` ledger/outbox abstractions. This project keeps the reusable ideas — profile catalog, policy gate, host-key pinning, trace writer, SSH.NET executor — and removes runtime-specific concepts from the MCP server boundary.

Agent routing belongs above this tool pack. See `docs/AGENT_ROUTER_DESIGN.md`.


## SSH profile and vault file locations

The canonical user-level SSH sidecar location is `%LOCALAPPDATA%\McpServer\ssh` on Windows. The sidecar writes profiles to `%LOCALAPPDATA%\McpServer\ssh\ssh-profiles.local.json`, vault metadata to `%LOCALAPPDATA%\McpServer\ssh\ssh-vault.local.json`, and the local vault key to `%LOCALAPPDATA%\McpServer\ssh\ssh-vault.key`. The MCP server reads that same LocalAppData profile file by default. A legacy Roaming AppData profile path is read only as a temporary compatibility fallback.
