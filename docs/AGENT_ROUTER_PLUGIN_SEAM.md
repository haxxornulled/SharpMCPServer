# AgentRouter Plugin Seam

The AgentRouter plugin seam is generic. SSH is the first practical plugin because it is high-risk, cancellable, traceable, and already exists in this repo.

## Responsibility split

- `MCPServer.AgentRouter.Hosting` owns the hosted lifecycle.
- `MCPServer.AgentRouter.Application` owns plugin selection and use-case orchestration.
- `MCPServer.AgentRouter.Domain` owns capability/risk concepts and run invariants.
- `MCPServer.AgentRouter.Ssh` adapts the generic plugin seam to the existing `ISshAgentRuntime`.
- `MCPServer.Tools.Ssh` continues to own SSH execution mechanics and MCP SSH tools.
- `MCPServer.Host` composes the modules through Autofac.

The AgentRouter core must not become SSH-aware. The SSH plugin registers as `IAgentPlugin` and advertises capabilities such as `remote-shell` and `ssh-agent`.

## First adapter

`SshAgentPlugin` maps a generic `AgentPluginExecutionRequest` into the existing `SshAgentLaunchRequest`.

Supported metadata keys:

- `ssh.profile`
- `ssh.command`
- `ssh.argumentsJson`
- `ssh.commandsJson`
- `ssh.workingDirectory`
- `ssh.timeoutSecondsPerStep`

This keeps the old SSH agent runtime intact while proving the AgentRouter plugin boundary.
