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

Solid arrows point from the owning boundary to the component it depends on.

```mermaid
flowchart TB
    classDef shell fill:#eef2ff,stroke:#6366f1,color:#1e1b4b,stroke-width:1.5px;
    classDef runtime fill:#ecfdf5,stroke:#22c55e,color:#14532d,stroke-width:1.5px;
    classDef storage fill:#fff7ed,stroke:#f97316,color:#7c2d12,stroke-width:1.5px;

    subgraph Shell["Composition shell"]
        direction TB
        SidecarCLI["MCPServer.Host.Sidecar"]:::shell
        Factory["SshHostSidecarRuntimeFactory"]:::shell
        RuntimeModule["SshHostSidecarRuntimeModule"]:::shell
        ProviderModule["SshProviderModule"]:::shell

        SidecarCLI --> Factory
        Factory --> Settings["SshToolSettings"]:::shell
        Factory --> RuntimeModule
        RuntimeModule --> ProviderModule
    end

    subgraph Runtime["SSH runtime"]
        direction LR
        Tools["MCPServer.Tools.Ssh"]:::runtime
        PathResolver["ISshPathResolver"]:::runtime
        ProfileStore["ISshProfileManagementStore"]:::runtime
        Vault["ISshCredentialVault"]:::runtime
        SshNet["SSH.NET execution"]:::runtime
    end

    subgraph Storage["SQLite-backed stores"]
        direction LR
        SqliteProfiles[(SQLite profile DB)]:::storage
        SqliteVault[(SQLite credential vault)]:::storage
    end

    Tools --> ProviderModule
    ProviderModule --> PathResolver
    ProviderModule --> ProfileStore
    ProviderModule --> Vault
    ProviderModule --> SshNet
    ProfileStore --> SqliteProfiles
    Vault --> SqliteVault
```

The sidecar is a composition shell over the SSH provider.
It should stay that way: CLI on the outside, provider runtime and SQLite-backed stores in `MCPServer.Ssh`, and MCP tool exposure in `MCPServer.Tools.Ssh`.
