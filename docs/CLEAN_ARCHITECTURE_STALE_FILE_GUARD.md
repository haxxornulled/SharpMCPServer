# Clean Architecture Stale File Guard

The JSON-RPC parser/serializer contracts are Application-layer ports:

- `MCPServer.Application/Mcp/JsonRpc/Interfaces/IJsonRpcMessageParser.cs`
- `MCPServer.Application/Mcp/JsonRpc/Interfaces/IJsonRpcResponseSerializer.cs`

Infrastructure owns only concrete adapters:

- `MCPServer.Infrastructure/Mcp/JsonRpc/JsonRpcMessageParser.cs`
- `MCPServer.Infrastructure/Mcp/JsonRpc/JsonRpcResponseSerializer.cs`

The stale duplicate source files that previously caused namespace ambiguity have been deleted from the source tree. Do not reintroduce these paths:

- `MCPServer.Application/Mcp/JsonRpc/IJsonRpcMessageParser.cs`
- `MCPServer.Application/Mcp/JsonRpc/IJsonRpcResponseSerializer.cs`
- `MCPServer.Infrastructure/Mcp/JsonRpc/IJsonRpcMessageParser.cs`
- `MCPServer.Infrastructure/Mcp/JsonRpc/IJsonRpcResponseSerializer.cs`

Project files should not use broad `<Compile Remove>` masks to hide stale files. If stale files appear again, delete them and fix the source organization rather than masking the problem.

Implementation files and registrations intentionally use fully qualified Application port namespaces where that prevents ambiguity.
