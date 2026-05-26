# MCPServer.ExecutionPlugins.Ssh

This package adapts the SSH provider runtime into the provider-neutral execution seam.

It is responsible for:
- registering the SSH-backed generic agent plugin
- translating provider-neutral execution requests into SSH runtime operations
- depending on `MCPServer.Execution.Abstractions` and `MCPServer.Ssh`

It is **not** responsible for:
- defining the execution seam itself
- owning SSH persistence or vault storage
- exposing MCP tools
- polluting `MCPServer.AgentRouter.*` with provider-specific concerns
