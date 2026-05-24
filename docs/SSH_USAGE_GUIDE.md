# SSH Usage Guide

This document is the operator guide for every SSH-related piece in this solution: the host sidecar, vault, profiles, direct execution tool, SSH agent tools, LM Studio wiring, and the future agent-router boundary.

## Mental model

```text
LM Studio / host app
  launches MCPServer.Host.Sidecar serve

MCPServer.Host.Sidecar
  owns local developer/operator UX
  owns the encrypted SSH vault
  owns profile file creation
  hydrates only referenced vault entries as env vars
  starts MCPServer.Host as a protocol-clean stdio child process

MCPServer.Host
  normal MCP server
  advertises the SSH tool surface at startup
  reads SSH execution settings through IOptionsMonitor<SshToolSettings>

MCPServer.Tools.Ssh
  MCP tool pack
  loads profiles
  enforces SSH execution policy
  executes SSH commands
  records execution traces
  runs background SSH agents
```

The sidecar is **not** an MCP tool. It is a host-side companion used to configure and launch the MCP server safely. The advertised MCP tools are provided by `MCPServer.Tools.Ssh`.

## Tool inventory

The server advertises these SSH tools through `tools/list`. Runtime execution is controlled by `McpTools:Ssh:Enabled` and is read through `IOptionsMonitor<SshToolSettings>`, so appsettings changes can affect policy without restarting the MCP server process:

| Tool | Purpose |
| --- | --- |
| `server.info` | Built-in server info smoke-test tool. |
| `ssh.profiles.list` | Lists configured SSH profiles without secrets. |
| `ssh.exec` | Runs one policy-checked SSH command. |
| `ssh.agent.launch` | Starts a background, pollable SSH command sequence. |
| `ssh.agent.status` | Reads current agent state, steps, and output tails. |
| `ssh.agent.output` | Reads incremental stdout/stderr by offset. |
| `ssh.agent.cancel` | Requests cancellation of a running SSH agent. |

If a client shows only `server.info`, the process is running an older build or did not load the SSH module. With the current host, SSH tools are registered even when execution is disabled; disabled execution returns a tool-level policy denial instead of hiding the tool surface.

## Storage locations

Default sidecar storage is user-local:

```text
%LOCALAPPDATA%/McpServer/ssh/ssh-vault.local.json
%LOCALAPPDATA%/McpServer/ssh/ssh-vault.key
%LOCALAPPDATA%/McpServer/ssh/ssh-profiles.local.json
```

On Linux/macOS, the same logical paths are resolved under the platform-specific local application data directory used by .NET.

Override paths when needed:

```powershell
--base-directory <dir>
--vault-path <path>
--vault-key-path <path>
--profile-path <path>
```

## Hot-reloadable SSH settings

`MCPServer.Host` binds `McpTools:Ssh` with `IOptionsMonitor<SshToolSettings>`. The SSH execution policy, profile path resolver, and trace writer read `CurrentValue` at use time. This is intentional for a future Visual Studio extension: the extension can update appsettings/profile/vault files and the running MCP server can pick up policy changes without bouncing the process.

Tool discovery is still startup-time MCP registration. The SSH tools remain advertised, while `Enabled=false` causes execution calls to return a normal tool-level denial.

## Build first

```powershell
dotnet restore .\MCPServer.slnx
dotnet build .\MCPServer.slnx -c Debug
dotnet test .\MCPServer.slnx -c Debug --no-build
```

## Sidecar help

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- --help
```

The sidecar command groups are:

```text
vault     low-level secret vault CRUD
profile   low-level profile CRUD
ssh       developer-friendly profile + vault workflows
run       diagnostic MCP client mode; human-readable stdout
serve     MCP client launch mode; protocol-clean stdio proxy
```

Use `serve` for LM Studio and other MCP clients. Use `run` only for manual diagnostics.

## Developer-friendly password profile

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  ssh add-password dev `
  --host 192.168.1.50 `
  --username james `
  --password "change-me" `
  --host-key-sha256 SHA256:REPLACE_WITH_PIN `
  --working-directory /home/james/work `
  --allowed-command pwd `
  --allowed-command whoami `
  --allowed-command uname `
  --allowed-command ls `
  --allowed-command git `
  --allowed-command dotnet `
  --allowed-prefix /home/james/work
```

Omit `--password` to be prompted without echoing. Use `--password-file` for automation.

The command creates:

1. a vault entry, default name `<profile>-password`;
2. an SSH profile that references the generated vault environment variable;
3. a profile file consumed by `MCPServer.Tools.Ssh`.

When the profile already exists, `ssh add-password` merges supplied values into the existing profile. Add `--replace true` or `--overwrite true` to rewrite the whole profile cleanly and reset omitted flags/lists to defaults.

## Private-key profiles

Password auth is the supported happy path right now. The sidecar still has `ssh add-key` plumbing for later, but the examples in this guide intentionally use `ssh add-password`. Do not switch operators to keypair workflows until we decide to harden and document that path.

## List and delete SSH profiles

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- ssh list
```

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- ssh list --output json
```

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- ssh delete dev
```

## Low-level vault commands

List vault entries without revealing secrets:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- vault list
```

Create/update a vault secret:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  vault set dev-password --secret "change-me" --description "dev box password"
```

Verify a vault secret:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  vault verify dev-password --expected "change-me"
```

Delete a vault entry:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- vault delete dev-password
```

Vault entries resolve to environment variable names like:

```text
MCPSERVER_SSH_VAULT_DEV_PASSWORD
```

Only variables referenced by configured profiles are hydrated into the child MCP server process.

## Low-level profile commands

The low-level profile commands are useful for automation or CI-style setup.

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  profile upsert dev `
  --host 192.168.1.50 `
  --username james `
  --password-vault-item dev-password `
  --host-key-sha256 SHA256:REPLACE_WITH_PIN `
  --working-directory /home/james/work `
  --allowed-command git `
  --allowed-command dotnet `
  --allowed-prefix /home/james/work
```

Link or relink existing vault entries:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- profile link-password dev dev-password
```

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- profile link-key-passphrase dev dev-key-passphrase
```

## Profile JSON shape

A typical profile looks like this:

```json
{
  "profiles": {
    "dev": {
      "host": "192.168.1.50",
      "port": 22,
      "username": "james",
      "passwordEnvironmentVariable": "MCPSERVER_SSH_VAULT_DEV_PASSWORD",
      "privateKeyPath": null,
      "privateKeyPassphraseEnvironmentVariable": null,
      "hostKeySha256": "SHA256:REPLACE_WITH_PIN",
      "acceptUnknownHostKey": false,
      "workingDirectory": "/home/james/work",
      "allowedCommands": ["pwd", "whoami", "uname", "ls", "git", "dotnet"],
      "deniedCommands": ["rm", "shutdown", "reboot", "sudo", "su"],
      "allowedRemotePathPrefixes": ["/home/james/work"],
      "allowSudoCommand": false,
      "allowAllCommands": false,
      "privileged": false,
      "allowedRoot": false
    }
  }
}
```

Do not commit real local profile files.

## Root profiles and allowedRoot

Root-capable AI automation is intentionally obnoxious to enable.

A root SSH profile requires all of these:

```text
username = root
allowedRoot = true
privileged = true
hostKeySha256 is pinned
acceptUnknownHostKey = false
```

For unrestricted root command execution, also set:

```text
allowAllCommands = true
```

The sidecar refuses to create or update a root-capable profile unless this explicit acknowledgement is supplied:

```powershell
--i-understand-root-ai-risk true
```

Example:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  ssh add-password debian-root-lab `
  --host 192.168.1.50 `
  --username root `
  --host-key-sha256 SHA256:REPLACE_WITH_PIN `
  --allowed-root true `
  --privileged true `
  --allow-all-commands true `
  --working-directory /root `
  --i-understand-root-ai-risk true
```

The tool policy still enforces host-key pinning and root opt-in at execution time, so hand-editing profile files does not bypass the root gate.

## Start for LM Studio or another MCP client

Use `serve`:

```powershell
MCPServer.Host.Sidecar.exe serve --server-path "C:\Visual Studio Projects\MCPServer\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe"
```

`serve` is protocol-clean. It forwards stdin/stdout to the child MCP server and writes diagnostics only to stderr.

Do not configure LM Studio to use `run`. `run` prints human-readable output to stdout and is only for diagnostics.

## Direct MCPServer.Host launch

Direct launch is fine when the parent process supplies config and secrets itself:

```text
McpTools__Ssh__Enabled=true
McpTools__Ssh__ProfilePath=C:\path\to\ssh-profiles.local.json
MCPSERVER_SSH_VAULT_DEV_PASSWORD=<secret>
```

The host sets its content root to `AppContext.BaseDirectory`, so appsettings beside `MCPServer.Host.exe` are loaded even when a GUI client launches from a different working directory.

## Smoke-test from sidecar diagnostic mode

List the tools the child server advertises:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  run --server-path .\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe
```

Call `ssh.profiles.list`:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  run `
  --server-path .\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe `
  --tool ssh.profiles.list `
  --arguments "{}"
```

Call `ssh.exec`:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  run `
  --server-path .\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe `
  --tool ssh.exec `
  --arguments '{"profile":"dev","command":"whoami","arguments":[],"workingDirectory":"/home/james/work"}'
```

## SSH agent workflow

Launch a background command sequence:

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

Poll status:

```json
{ "agentId": "ssh-agent-..." }
```

Read incremental output:

```json
{
  "agentId": "ssh-agent-...",
  "stdoutOffset": 0,
  "stderrOffset": 0,
  "maxChars": 20000
}
```

Use the returned `nextStdoutOffset` and `nextStderrOffset` on the next output call.

Cancel:

```json
{ "agentId": "ssh-agent-..." }
```

Cancellation is cooperative. If SSH.NET is already blocked inside a remote command, cancellation marks the agent cancelled and requests cancellation, but the remote process may continue until the SSH command returns or times out.

## Current agent model vs MCP native tasks

The current SSH agents are implemented as normal MCP tools:

```text
ssh.agent.launch  -> returns agentId
ssh.agent.status  -> poll
ssh.agent.output  -> tail stdout/stderr
ssh.agent.cancel  -> cancel request
```

The server does **not** currently advertise `capabilities.tasks`. Native MCP tasks require additional protocol handlers such as `tasks/get`, `tasks/result`, `tasks/list`, and `tasks/cancel`, plus task capability negotiation. See `docs/AGENT_ROUTER_DESIGN.md` for the proposed next step.

## Troubleshooting

### LM Studio shows only server.info

The SSH tool module was not enabled in the launched process. Use sidecar `serve`, or launch `MCPServer.Host` with:

```text
McpTools__Ssh__Enabled=true
McpTools__Ssh__ProfilePath=<absolute profile path>
```

Also confirm that the executable is the latest rebuilt binary.

### Profile appears but execution fails with missing secret

The profile references an environment variable that was not hydrated. Use sidecar `serve` for vault-backed profiles, or manually provide the referenced `MCPSERVER_SSH_VAULT_*` variable in the parent environment.

### Host key failure

Set `hostKeySha256` to the server's pinned SHA256 fingerprint. Do not turn on unknown host keys except in disposable local labs.

### Working directory rejected

Add the working directory or a parent prefix through `--allowed-prefix` or `allowedRemotePathPrefixes`.

### Command rejected

Add the executable name with `--allowed-command`, remove it from `deniedCommands`, or use a root/privileged `allowAllCommands` profile only when that risk is explicitly acceptable.


## SSH profile and vault file locations

The canonical user-level SSH sidecar location is `%LOCALAPPDATA%\McpServer\ssh` on Windows. The sidecar writes profiles to `%LOCALAPPDATA%\McpServer\ssh\ssh-profiles.local.json`, vault metadata to `%LOCALAPPDATA%\McpServer\ssh\ssh-vault.local.json`, and the local vault key to `%LOCALAPPDATA%\McpServer\ssh\ssh-vault.key`. The MCP server reads that same LocalAppData profile file by default. A legacy Roaming AppData profile path is read only as a temporary compatibility fallback.
