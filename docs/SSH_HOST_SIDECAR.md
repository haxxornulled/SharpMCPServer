# SSH Host Sidecar

`MCPServer.Host.Sidecar` is a host-side companion executable for SSH profile and credential management.

It is intentionally **not** an MCP server tool. It belongs on the host/client side of the architecture:

- the MCP server stays focused on protocol primitives and tool execution;
- the host sidecar owns local credential/profile lifecycle;
- the sidecar starts `MCPServer.Host` as a child process and hydrates only the environment variables referenced by configured SSH profiles;
- credentials are never accepted through `tools/call` arguments.

For the complete operator flow, see `docs/SSH_USAGE_GUIDE.md`.

## Commands

```text
vault list
vault set <name>
vault delete <name>
vault verify <name>

profile list
profile upsert <name>
profile replace <name>
profile link-password <profile> <vault-item>
profile link-key-passphrase <profile> <vault-item>
profile delete <name>

ssh add-password <profile> [--replace true]
ssh list
ssh delete <profile>

# Present but not the current happy path:
ssh add-key <profile> [--replace true]

run --server-path <MCPServer.Host.exe>
serve --server-path <MCPServer.Host.exe>
```

Use `ssh add-password` for normal developer setup. Password auth is the current supported path. `ssh add-key` exists for later keypair support, but the current operator flow should stick to vault-backed passwords. Use `vault` and `profile` directly for automation or repair.

## Runtime settings reload

The MCP host binds `McpTools:Ssh` through `IOptionsMonitor<SshToolSettings>`. The sidecar writes profile and vault files, and `serve` launches the MCP host with the correct environment. This keeps the door open for a Visual Studio extension to edit settings/profiles without forcing the MCP server process to restart.

## Storage

Default files live under the user-local application data directory:

```text
%LOCALAPPDATA%/McpServer/ssh/ssh-vault.local.json
%LOCALAPPDATA%/McpServer/ssh/ssh-vault.key
%LOCALAPPDATA%/McpServer/ssh/ssh-profiles.local.json
```

Override paths:

```powershell
--base-directory <dir>
--vault-path <path>
--vault-key-path <path>
--profile-path <path>
```

## Developer-friendly password profile

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  ssh add-password dev `
  --host 192.168.1.50 `
  --username james `
  --password "change-me" `
  --host-key-sha256 SHA256:REPLACE_WITH_PIN `
  --working-directory /home/james/work `
  --allowed-command dotnet `
  --allowed-command git `
  --allowed-command ls `
  --allowed-prefix /home/james/work
```

Omit `--password` to prompt interactively. Use `--password-file` for automation.

## Private-key profiles

Password auth is the supported happy path right now. `ssh add-key` remains available as plumbing for future work, but this sidecar guide intentionally uses `ssh add-password` for the current workflow.

## Updating versus replacing profiles

`profile upsert`, `ssh add-password`, and `ssh add-key` merge supplied values into an existing profile. This is useful for changing one field without losing command allowlists or path prefixes.

Use `--replace true` or `--overwrite true` with the developer-friendly `ssh` commands when you want a clean profile rewrite:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  ssh add-password debian-root-lab `
  --replace true `
  --host 173.255.205.169 `
  --username root `
  --host-key-sha256 SHA256:REAL_HOST_KEY_PIN `
  --working-directory /root `
  --allowed-root true `
  --privileged true `
  --allow-all-commands true `
  --i-understand-root-ai-risk true
```

For low-level automation, `profile replace` rewrites the profile object instead of merging with the previous one:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  profile replace debian-root-lab `
  --host 173.255.205.169 `
  --username root `
  --password-vault-item debian-root-lab-password `
  --host-key-sha256 SHA256:REAL_HOST_KEY_PIN `
  --working-directory /root `
  --allowed-root true `
  --privileged true `
  --allow-all-commands true `
  --i-understand-root-ai-risk true
```

Use `ssh delete <profile>` when you want to remove old test profiles such as `dev` or `dev-key`.

## Low-level vault commands

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- vault list
```

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  vault set dev-password --secret "change-me" --description "dev box password"
```

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- vault verify dev-password --expected "change-me"
```

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- vault delete dev-password
```

Vault entries map to generated environment variables:

```text
MCPSERVER_SSH_VAULT_DEV_PASSWORD
```

Secrets are decrypted only by the sidecar and injected only into the child server process when referenced by a profile.

## Low-level profile commands

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- profile list
```

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  profile upsert dev `
  --host 192.168.1.50 `
  --username james `
  --password-vault-item dev-password `
  --host-key-sha256 SHA256:REPLACE_WITH_PIN `
  --working-directory /home/james/work `
  --allowed-command dotnet `
  --allowed-command git `
  --allowed-prefix /home/james/work
```

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- profile link-password dev dev-password
```

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- profile link-key-passphrase dev dev-key-passphrase
```

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- profile delete dev
```

## allowedRoot

Profiles support an explicit `allowedRoot` switch for the dangerous case where the profile uses `username: "root"`.

Root SSH automation is blocked unless all of these are true:

- the profile username is `root`;
- `allowedRoot` is `true`;
- `privileged` is `true`;
- a `hostKeySha256` pin is configured;
- unknown host keys are not accepted.

For unrestricted root command execution, `allowAllCommands=true` is also required.

Creating or updating a root-capable profile through the sidecar requires an explicit acknowledgement:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  ssh add-password root-dev `
  --host 192.168.1.50 `
  --username root `
  --host-key-sha256 SHA256:REPLACE_WITH_PIN `
  --allowed-root true `
  --privileged true `
  --allow-all-commands true `
  --i-understand-root-ai-risk true
```

The sidecar prints a prominent warning box to stderr. The server-side policy enforces the same rule at execution time, so manually edited profile files do not bypass it.

## Start MCP through the sidecar for diagnostics

Use `run` to start the child server, initialize a client session, and print human-readable diagnostics:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  run --server-path .\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe
```

Call an SSH tool through the diagnostic client:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  run `
  --server-path .\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe `
  --tool ssh.exec `
  --arguments '{"profile":"dev","command":"ls","arguments":["-la"],"workingDirectory":"/home/james/work"}'
```

Do not use `run` as an MCP client command in LM Studio because it writes human-readable text to stdout.

## MCP client launch mode

Use `serve` when configuring an MCP client such as LM Studio:

```powershell
MCPServer.Host.Sidecar.exe serve --server-path "C:\Visual Studio Projects\MCPServer\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe"
```

`serve` is a protocol-clean stdio proxy. It hydrates the SSH profile/vault environment and forwards the MCP client's stdin/stdout to the child `MCPServer.Host` process without adding human text to stdout.

The sidecar injects:

```text
McpTools__Ssh__Enabled=true
McpTools__Ssh__ProfilePath=<sidecar-profile-path>
MCPSERVER_SSH_VAULT_<ITEM_NAME>=<decrypted secret>
```

Only referenced vault variables are hydrated into the child server process.

## Design constraints

- Keep SSH credentials out of MCP request payloads.
- Keep vault/profile management out of the MCP server core.
- Keep `MCPServer.Tools.Ssh` optional and capability-honest.
- Use `ValueTask` for async sidecar storage APIs.
- Use `System.Text.Json` source generation for sidecar persistence.
- Require explicit acknowledgement before creating root-capable profiles.
- Keep sidecar `serve` protocol-clean for stdio clients.

## Related docs

- `docs/SSH_USAGE_GUIDE.md`
- `docs/SSH_TOOLS.md`
- `docs/SSH_AGENTS.md`
- `docs/LM_STUDIO_SSH_TOOLS.md`
- `docs/AGENT_ROUTER_DESIGN.md`


## SSH profile and vault file locations

The canonical user-level SSH sidecar location is `%LOCALAPPDATA%\McpServer\ssh` on Windows. The sidecar writes profiles to `%LOCALAPPDATA%\McpServer\ssh\ssh-profiles.local.json`, vault metadata to `%LOCALAPPDATA%\McpServer\ssh\ssh-vault.local.json`, and the local vault key to `%LOCALAPPDATA%\McpServer\ssh\ssh-vault.key`. The MCP server reads that same LocalAppData profile file by default. A legacy Roaming AppData profile path is read only as a temporary compatibility fallback.
