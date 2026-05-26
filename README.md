# MCPServer

MCPServer is a .NET 10 repository built around a stdio Model Context Protocol host, an SSH provider/tool-pack, a host-side sidecar CLI, and an evolving AgentRouter bounded context.

This README is intentionally short. Historical design notes that no longer matched the codebase were removed instead of being preserved as misleading documentation.

## Build

```powershell
dotnet restore .\MCPServer.slnx
dotnet build .\MCPServer.slnx -c Debug
dotnet test .\MCPServer.slnx -c Debug
```

## Main executable

```powershell
dotnet run --project .\MCPServer.Host\MCPServer.Host.csproj
```

## Sidecar CLI

```powershell
dotnet run --project .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj -- --help
```

## Where to read next

- `docs/REPO_ARCHITECTURE.md`
- `docs/BUILD_AND_TEST.md`
- `docs/INSTALL.md`
- `docs/SSH_BOUNDARY.md`
- `docs/AGENT_ROUTER_BOUNDARY.md`
- `docs/KNOWN_DRIFT.md`

## Repository rules

- `System.Text.Json` only
- Autofac for composition
- no MediatR
- explicit boundaries over magic dispatch
- fix the first real failure before chasing downstream metadata errors
