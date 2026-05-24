# Build and Test

Use these commands from the `MCPServer` repository root.

```powershell
dotnet restore .\MCPServer.slnx
dotnet build .\MCPServer.slnx -c Release --no-restore
dotnet test .\MCPServer.slnx -c Release --no-build
```

For a local stdio smoke run, build first, then launch the host and send newline-delimited JSON-RPC messages on stdin. Keep stdout reserved for MCP protocol frames; diagnostics belong on stderr.

```powershell
dotnet run --project .\MCPServer.Host\MCPServer.Host.csproj -c Release --no-build
```

Minimal conversation:

```jsonl
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"manual-smoke","version":"1"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"ping"}
{"jsonrpc":"2.0","id":3,"method":"tools/list"}
{"jsonrpc":"2.0","id":4,"method":"logging/setLevel","params":{"level":"warning"}}
{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"server.info","arguments":{}}}
```


Targeted test runs:

```powershell
dotnet test .\MCPServer.UnitTests\MCPServer.UnitTests.csproj -c Release
dotnet test .\MCPServer.ProtocolTests\MCPServer.ProtocolTests.csproj -c Release
```
