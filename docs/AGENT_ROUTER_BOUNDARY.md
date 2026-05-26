# AgentRouter Boundary

## AgentRouter core packages

- `MCPServer.AgentRouter.Abstractions`
- `MCPServer.AgentRouter.Domain`
- `MCPServer.AgentRouter.Application`
- `MCPServer.AgentRouter.Infrastructure`
- `MCPServer.AgentRouter.Hosting`

## Not part of AgentRouter core

- `MCPServer.Execution.Abstractions`
- `MCPServer.ExecutionPlugins.Ssh`
- `MCPServer.Ssh`
- `MCPServer.Tools.Ssh`

## Rules

- AgentRouter core contains no provider-specific nouns or contracts.
- Default/no-op router composition lives in Hosting, not a fake `Defaults` layer.
- SSH-backed execution is an outer integration, not an AgentRouter package.
