# Clean Architecture Boundary Notes

## Decision

Application owns MCP ports/contracts that application and transport code depend on. Infrastructure owns concrete adapters.

## Current boundary

- `MCPServer.Application/Mcp/JsonRpc/Interfaces/IJsonRpcMessageParser.cs` is the parser port.
- `MCPServer.Application/Mcp/JsonRpc/Interfaces/IJsonRpcResponseSerializer.cs` is the serializer port.
- `MCPServer.Application/Mcp/Interfaces/*` contains application-layer service ports such as registries, lifecycle/session state, request dispatch, and tool/resource/prompt contracts.
- `MCPServer.Infrastructure/Mcp/JsonRpc/JsonRpcMessageParser.cs` is the concrete `System.Text.Json` implementation.
- `MCPServer.Infrastructure/Mcp/JsonRpc/JsonRpcResponseSerializer.cs` is the concrete pooled UTF-8 writer implementation.
- `MCPServer.Infrastructure/Mcp/Stdio/*` remains infrastructure because it is an I/O adapter around process stdin/stdout.

## Rule going forward

If an abstraction represents what the application needs, it belongs in Application under an explicit `Interfaces` folder. If a type touches external I/O, framework hosting, process streams, serialization mechanics, pooling mechanics, file/network/console access, schema-library mechanics, or vendor-specific infrastructure, it belongs in Infrastructure.

## Stale file policy

Stale duplicate source files must be deleted, not hidden with broad `<Compile Remove>` masks. Project files should not silently exclude future source files because that makes drift harder to detect in Visual Studio, Codex, and CI.

## Interface folder rule

Application-layer ports live under explicit `Interfaces` folders. Infrastructure implements those Application ports and must not define public interface contracts for Application-owned behavior. See `docs/INTERFACE_FOLDER_ORGANIZATION.md`.
