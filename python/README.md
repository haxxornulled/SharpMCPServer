# MCPServer AgentRouter Python Bridge

This package is the Python-facing wrapper for the NativeAOT AgentRouter bridge.
It stays intentionally small:

- `ctypes` only
- JSON in, JSON out
- explicit native-library path resolution
- no CLR bootstrap, no pythonnet, no direct C# object model exposure

## Install

If you are consuming the Python wrapper from this repository directly:

```bash
pip install -e python
```

If you are vendoring it from Git, point pip at the `python/` subdirectory:

```bash
pip install "git+https://github.com/<your-org>/MCPServer.git#subdirectory=python"
```

## Native library

The wrapper expects the NativeAOT shared library to be published separately.
Set the path explicitly:

```powershell
$env:MCP_SERVER_AGENTROUTER_NATIVE_LIBRARY = "C:\path\to\MCPServer.AgentRouter.PythonBridge.Native.dll"
```

On non-Windows platforms, use the platform-specific shared library filename that `dotnet publish` produces.

The wrapper also searches a few conventional locations, including a `native/` folder next to the package.

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

If you want the raw JSON boundary instead of Python dictionaries, use `run_json(...)`.

## Smoke test

If the native library path is configured, you can run the smoke test directly:

```bash
python -m unittest discover -s python/tests -p "test_native_smoke.py"
```
