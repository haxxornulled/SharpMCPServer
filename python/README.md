# MCPServer AgentRouter Python Bridge

This package is the Python-facing wrapper for the NativeAOT AgentRouter bridge.
It stays intentionally small:

- `ctypes` only
- JSON in, JSON out
- explicit native-library path resolution
- no CLR bootstrap, no pythonnet, no direct C# object model exposure

## Install and release flow

The canonical .NET-to-Python install instructions live in
[docs/INSTALL.md](../docs/INSTALL.md). That document starts with the .NET
build/publish path, then shows how to sync the NativeAOT bridge and install the
resulting wheel.

The wrapper will discover the copied native library automatically. You can also
set the explicit override if you want to point at a different build:

```powershell
$env:MCP_SERVER_AGENTROUTER_NATIVE_LIBRARY = "C:\path\to\MCPServer.AgentRouter.PythonBridge.Native.dll"
```

On non-Windows platforms, use the platform-specific shared library filename
that `dotnet publish` produces.

## Example

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

If you want the raw JSON boundary instead of Python dictionaries, use
`run_json(...)`.

## Smoke test

If the native library has been synced into the package directory, you can run
the smoke test directly. The install guide shows the clean-directory version
that exercises the installed wheel instead of the source tree.
