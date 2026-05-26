# MCPServer AgentRouter Python Bridge

This project publishes the AgentRouter bridge as a NativeAOT shared library with a tiny C ABI.

## Exported functions

- `agent_router_run(byte* inputUtf8, int inputLength, byte** outputUtf8, int* outputLength) -> int`
- `agent_router_free(byte* ptr) -> void`
- `agent_router_shutdown() -> int`

## JSON contract

Request:

```json
{
  "objective": "review the workspace",
  "metadata": {
    "agent.workflowMode": "deterministic"
  }
}
```

Response:

```json
{
  "status": "completed",
  "message": "Prepared local-model route for objective 'review the workspace' with 4 planning step(s).",
  "runId": null,
  "startedAtUtc": "2026-05-25T18:00:00Z",
  "completedAtUtc": "2026-05-25T18:00:00Z"
}
```

## Python shape

Use `ctypes` or `cffi` to call the exported C ABI and free the returned UTF-8 buffer with `agent_router_free`.

The Python side should treat the response buffer as owned by the native bridge until it calls the free export.

For a ready-made Python wrapper, see the standalone package under [`python/`](../python/). It exposes a thin `ctypes` client and expects the native library path to be provided explicitly or via `MCP_SERVER_AGENTROUTER_NATIVE_LIBRARY`.

## Publish

Publish the library as NativeAOT for the runtime identifier you need, for example:

```bash
dotnet publish MCPServer.AgentRouter.PythonBridge.Native/MCPServer.AgentRouter.PythonBridge.Native.csproj -c Release -r win-x64
```
