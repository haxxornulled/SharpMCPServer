# LM Studio SSH Tool Exposure

LM Studio discovers MCP tools through the MCP `tools/list` request after the server has completed `initialize` and received `notifications/initialized`.

The host sidecar administrative commands (`vault`, `profile`, `ssh`, and `run`) are not MCP tools. They are developer/operator UX.

## Preferred launch command

Use the sidecar `serve` command when SSH profiles reference vault-backed secrets:

```powershell
MCPServer.Host.Sidecar.exe serve --server-path "C:\Visual Studio Projects\MCPServer\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe"
```

`serve` is a protocol-clean stdio proxy. It writes only child MCP server protocol frames to stdout and forwards diagnostics to stderr.

The sidecar injects:

```text
McpTools__Ssh__Enabled=true
McpTools__Ssh__ProfilePath=<sidecar profile path>
MCPSERVER_SSH_VAULT_* secrets referenced by profiles
```

When connected through this command, LM Studio should see:

```text
server.info
ssh.exec
ssh.profiles.list
ssh.agent.launch
ssh.agent.status
ssh.agent.output
ssh.agent.cancel
```

## Direct host launch

Use direct host launch only when the required environment variables are supplied by LM Studio or the parent process.

Required environment variables:

```text
McpTools__Ssh__Enabled=true
McpTools__Ssh__ProfilePath=<absolute path to ssh-profiles.local.json>
```

If profiles reference vault variables, also provide the matching `MCPSERVER_SSH_VAULT_*` environment variables.

The host content root is pinned to `AppContext.BaseDirectory`, so appsettings beside `MCPServer.Host.exe` are loaded even if LM Studio launches from another working directory.

## Not for LM Studio: sidecar run

The sidecar `run` command is a human diagnostics/client command. It prints human-readable text to stdout, so it is not a valid MCP server command for LM Studio.

Use `serve` for LM Studio.

## If only server.info appears

The SSH module was not registered in the process LM Studio launched. Check:

1. LM Studio is launching the latest rebuilt executable.
2. LM Studio is using `MCPServer.Host.Sidecar.exe serve`, not `run`.
3. The `--server-path` points to the rebuilt `MCPServer.Host.exe`.
4. If launching `MCPServer.Host.exe` directly, `McpTools__Ssh__Enabled=true` is present in the launched process environment.
5. `McpTools__Ssh__ProfilePath` points to an existing profile file.
6. Vault-backed profiles have their referenced `MCPSERVER_SSH_VAULT_*` variables available, or are launched through `serve`.

## Quick validation with the sidecar diagnostic client

Before LM Studio, verify the same child server process manually:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  run --server-path .\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe
```

Expected output includes the SSH tools listed above. Then test a profile call:

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- `
  run `
  --server-path .\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe `
  --tool ssh.profiles.list `
  --arguments "{}"
```


## SSH profile and vault file locations

The canonical user-level SSH sidecar location is `%LOCALAPPDATA%\McpServer\ssh` on Windows. The sidecar writes profiles to `%LOCALAPPDATA%\McpServer\ssh\ssh-profiles.local.json`, vault metadata to `%LOCALAPPDATA%\McpServer\ssh\ssh-vault.local.json`, and the local vault key to `%LOCALAPPDATA%\McpServer\ssh\ssh-vault.key`. The MCP server reads that same LocalAppData profile file by default. A legacy Roaming AppData profile path is read only as a temporary compatibility fallback.
