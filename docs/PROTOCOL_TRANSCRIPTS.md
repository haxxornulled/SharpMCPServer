# MCP Protocol Transcript Examples

These examples are newline-delimited JSON messages for the stdio transport. stdout must contain only MCP JSON-RPC frames; process logs belong on stderr.

## Initialize and mark session ready

Client request:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"dev-client","version":"1.0.0"}}}
```

Server response shape:

```json
{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","capabilities":{"tools":{"listChanged":false},"logging":{}},"serverInfo":{"name":"MCPServer","version":"0.1.0"},"instructions":"MCPServer Phase 1 stdio server. Initialize, send notifications/initialized, then call tools/list or tools/call."}}
```

Client notification:

```json
{"jsonrpc":"2.0","method":"notifications/initialized"}
```

The server sends no response to this notification.

## Ping

```json
{"jsonrpc":"2.0","id":2,"method":"ping"}
```

Expected response shape:

```json
{"jsonrpc":"2.0","id":2,"result":{}}
```

## List tools

```json
{"jsonrpc":"2.0","id":3,"method":"tools/list"}
```

Expected response shape:

```json
{"jsonrpc":"2.0","id":3,"result":{"tools":[{"name":"server.info","title":"Server Information","description":"Returns basic information about this MCP server implementation.","inputSchema":{"type":"object","additionalProperties":false}}]}}
```

## Call server.info

```json
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"server.info","arguments":{}}}
```

Expected response shape:

```json
{"jsonrpc":"2.0","id":4,"result":{"content":[{"type":"text","text":"MCPServer phase 1 is online. Supported protocol version: 2025-11-25."}],"isError":false,"structuredContent":{"name":"MCPServer","protocolVersion":"2025-11-25","phase":"1","capabilities":["initialize","notifications/initialized","ping","tools/list","tools/call"]}}}
```

## Tool argument validation failure

The `server.info` tool declares `additionalProperties=false`. Unexpected arguments are reported as MCP tool errors, not JSON-RPC protocol failures.

```json
{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"server.info","arguments":{"unexpected":true}}}
```

Expected response shape:

```json
{"jsonrpc":"2.0","id":5,"result":{"content":[{"type":"text","text":"Tool argument does not satisfy JSON Schema: $ additionalProperties: Additional properties are not allowed"}],"isError":true}}
```

## Set MCP log level

```json
{"jsonrpc":"2.0","id":6,"method":"logging/setLevel","params":{"level":"warning"}}
```

Expected response shape:

```json
{"jsonrpc":"2.0","id":6,"result":{}}
```
