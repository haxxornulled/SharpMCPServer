# MCPServer.Tools.Ssh

This package is the MCP adapter surface for SSH capabilities.

It is responsible for:
- MCP tool registration and exposure
- adapting SSH provider functionality into MCP tools

It is **not** responsible for:
- owning SSH persistence, credential resolution, or vault storage
- AgentRouter execution abstractions
- host orchestration policy
