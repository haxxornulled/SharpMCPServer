# MCP client/host architecture

The repo now keeps the two sides of the MCP architecture separate:

- `MCPServer.Host` is the server process. It exposes MCP primitives such as tools, resources, prompts, and completion over stdio.
- `MCPServer.Client` is a lightweight client library. It owns one stateful session with one server process.
- `MCPServer.Client.Console` is a sample host-side executable that starts `MCPServer.Host`, performs `initialize`, sends `notifications/initialized`, lists tools, and calls a tool.

This follows the MCP host/client/server model: the host creates client instances, each client maintains an isolated 1:1 connection with a server, and servers provide focused capabilities.

## Async policy

The client surface is async-first:

```csharp
ValueTask<Fin<InitializeResult>> InitializeAsync(CancellationToken cancellationToken);
ValueTask<Fin<ToolsListResult>> ListToolsAsync(string? cursor, CancellationToken cancellationToken);
ValueTask<Fin<ToolCallResult>> CallToolAsync(string name, JsonElement? arguments, CancellationToken cancellationToken);
```

`ValueTask` is used on reusable abstractions where implementations may complete synchronously or asynchronously. Concrete I/O still awaits real stream/process work.

## Running the sample client

Build the solution first:

```powershell
dotnet build .\MCPServer.slnx -c Debug
```

Then run the client against the server executable:

```powershell
dotnet run --project .\MCPServer.Client.Console\MCPServer.Client.Console.csproj -- `
  --server-path ".\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe"
```

Call a specific tool:

```powershell
dotnet run --project .\MCPServer.Client.Console\MCPServer.Client.Console.csproj -- `
  --server-path ".\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe" `
  --tool server.info `
  --arguments "{}"
```

After enabling SSH tools and configuring profiles, list SSH profiles:

```powershell
dotnet run --project .\MCPServer.Client.Console\MCPServer.Client.Console.csproj -- `
  --server-path ".\MCPServer.Host\bin\Debug\net10.0\MCPServer.Host.exe" `
  --tool ssh.profiles.list `
  --arguments "{}"
```

The client is deliberately sequential. It does not pipeline JSON-RPC requests yet, which keeps request/response correlation simple for Phase 1 validation.


## Host sidecar

`MCPServer.Host.Sidecar` is a host-side companion that uses `MCPServer.Client` to spawn and talk to `MCPServer.Host` over stdio. It owns SSH vault/profile management, then hydrates referenced vault secrets as child-process environment variables before the MCP initialization handshake. This keeps credential lifecycle outside the MCP server while preserving the server's focused tool surface.

## Agent Router note

A future Agent Router should be implemented on the host/client side first. The MCP architecture gives the host responsibility for creating and managing clients, permissions, lifecycle, security policy, consent, model integration, and context aggregation. That makes the host/router the right place to turn high-level objectives into tool calls across one or more MCP server sessions.

For this repository, the intended split is:

```text
MCPServer.Host.Sidecar
  configure SSH vault/profile data
  launch MCPServer.Host in protocol-clean serve mode

MCPServer.Client
  maintain one stdio session with one MCP server

Future AgentRouter.Host
  maintain one or more MCPServer.Client sessions
  route objectives to tools/agents
  coordinate approvals and status UI

MCPServer.Tools.Ssh
  expose focused SSH execution and SSH agent tools
```

See `docs/AGENT_ROUTER_DESIGN.md` for the proposed router design and the path toward native MCP task support.
