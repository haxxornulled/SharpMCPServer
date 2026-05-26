# SSH Boundary

## Ownership

`MCPServer.Ssh` owns:
- SSH runtime
- execution policy
- profile storage
- credential vault
- credential resolution
- SSH.NET execution

`MCPServer.Tools.Ssh` owns:
- MCP tool adapters only

`MCPServer.ExecutionPlugins.Ssh` owns:
- adapting SSH-backed execution into provider-neutral execution contracts

## Rules

- No SSH concerns belong in AgentRouter core packages.
- SSH must not be modeled as an AgentRouter layer.
- SSH must not depend on MCP tool abstractions for runtime behavior.

## SSH flow at a glance

```mermaid
flowchart LR
    SidecarCLI["MCPServer.Host.Sidecar"]
    Factory["SshHostSidecarRuntimeFactory"]
    RuntimeModule["SshHostSidecarRuntimeModule"]
    ProviderModule["SshProviderModule"]
    Tools["MCPServer.Tools.Ssh"]
    Settings["SshToolSettings"]
    PathResolver["ISshPathResolver"]
    ProfileStore["ISshProfileManagementStore"]
    Vault["ISshCredentialVault"]
    SqliteProfiles[(SQLite profile DB)]
    SqliteVault[(SQLite credential vault)]
    SshNet["SSH.NET execution"]

    SidecarCLI --> Factory
    Factory --> Settings
    Factory --> RuntimeModule
    RuntimeModule --> ProviderModule
    ProviderModule --> PathResolver
    ProviderModule --> ProfileStore
    ProviderModule --> Vault
    ProviderModule --> SshNet
    Tools --> ProviderModule
    ProfileStore --> SqliteProfiles
    Vault --> SqliteVault
```

The sidecar is a composition shell over the SSH provider.
It should stay that way: CLI on the outside, provider runtime and SQLite-backed stores in `MCPServer.Ssh`, and MCP tool exposure in `MCPServer.Tools.Ssh`.
