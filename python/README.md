# MCPServer AgentRouter Python Bridge

This package is the Python-facing wrapper for the NativeAOT AgentRouter bridge.
It stays intentionally small:

- `ctypes` only
- JSON in, JSON out
- explicit native library resolution
- no CLR bootstrap, no `pythonnet`, no direct C# object model exposure

## Quick start

```python
from mcpserver_agentrouter_bridge import AgentRouterBridge

with AgentRouterBridge() as bridge:
    response = bridge.run({
        "objective": "review the workspace",
        "metadata": {"agent.workflowMode": "deterministic"},
    })

print(response["status"])
print(response["message"])
```

If you want the raw UTF-8 JSON boundary instead of Python dictionaries, use
`run_json(...)`.

## Native library resolution

The wrapper looks for the copied NativeAOT library automatically in the package
and nearby working directories.

If you need to point at a different build, set the explicit override:

```powershell
$env:MCP_SERVER_AGENTROUTER_NATIVE_LIBRARY = "C:\path\to\MCPServer.AgentRouter.PythonBridge.Native.dll"
```

On non-Windows platforms, use the shared library name produced by
`dotnet publish`.

## Install and release flow

The canonical .NET-to-Python install instructions live in
[docs/INSTALL.md](../docs/INSTALL.md).

That document starts with the .NET build/publish path, then shows how to sync
the NativeAOT bridge and install the resulting wheel.

## Smoke test

If the native library has already been synced into the package-local `native/`
folder, you can run the bridge directly from the source tree.

For the clean-directory smoke test that exercises the installed wheel instead
of the source tree, see [docs/INSTALL.md](../docs/INSTALL.md).

