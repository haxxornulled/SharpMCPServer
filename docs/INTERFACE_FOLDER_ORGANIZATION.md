# Interface Folder Organization

Clean Architecture rule for this project:

- Interface contracts owned by a layer live under an explicit `Interfaces` folder in that layer.
- Application owns MCP ports/contracts. Infrastructure implements adapters and must not define public interfaces.
- Namespace names match the folder boundary so accidental dependencies are visible in code review.

Current contract layout:

```text
MCPServer.Application/Mcp/Interfaces
MCPServer.Application/Mcp/JsonRpc/Interfaces
```

Infrastructure concrete adapters implement Application ports:

```text
MCPServer.Infrastructure/Mcp/JsonRpc/JsonRpcMessageParser.cs
MCPServer.Infrastructure/Mcp/JsonRpc/JsonRpcResponseSerializer.cs
MCPServer.Infrastructure/Mcp/Stdio/StdioMcpServerService.cs
```

The Application project has stale-file guards for the old root interface locations so updating an existing working tree in place does not compile duplicate contracts.
