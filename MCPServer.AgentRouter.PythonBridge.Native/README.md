# MCPServer AgentRouter Native Bridge

This project publishes the AgentRouter bridge as a NativeAOT shared library
with a small C ABI.

## Exported ABI

- `agent_router_run(byte* inputUtf8, int inputLength, byte** outputUtf8, int* outputLength) -> int`
- `agent_router_free(byte* ptr) -> void`
- `agent_router_shutdown() -> int`

## Contract

The bridge speaks JSON over UTF-8 bytes.

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

## Ownership

The native bridge owns the returned UTF-8 buffer until the caller releases it
with `agent_router_free`.

Callers should treat the ABI as:

- UTF-8 bytes in
- UTF-8 bytes out
- integer status codes
- explicit release of returned memory

## Python

Use `ctypes` or `cffi` to load the shared library from Python.

For the Python wrapper, see [`python/`](../python/). The install and release
flow lives in [`docs/INSTALL.md`](../docs/INSTALL.md), and the package-local
native binary is synced by [`scripts/Sync-PythonBridge.ps1`](../scripts/Sync-PythonBridge.ps1).

