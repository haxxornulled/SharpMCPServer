# Sidecar Composition Boundary

`MCPServer.Host.Sidecar` owns application-edge composition for the sidecar/admin process.

Rules:

- `Program.cs` should remain focused on command dispatch and CLI behavior.
- Sidecar runtime wiring belongs in sidecar-owned composition modules or factories.
- SSH provider concerns should still be resolved through `MCPServer.Ssh`; the sidecar should not grow a second persistence/runtime subsystem.
- Path/display-path resolution and runtime creation should stay behind sidecar-owned composition helpers.

Current direction:

- `SshHostSidecarRuntimeModule` owns sidecar SSH runtime registrations.
- `SshHostSidecarRuntimeFactory` owns sidecar runtime creation and display-path resolution.
