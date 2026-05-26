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

For a ready-made Python wrapper, see the standalone package under [`python/`](../python/). The canonical .NET-to-Python install path for that package lives in [`docs/INSTALL.md`](../docs/INSTALL.md), and the native library is copied into the package-local `native/` folder by [`scripts/Sync-PythonBridge.ps1`](../scripts/Sync-PythonBridge.ps1).
