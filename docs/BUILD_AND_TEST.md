# Build and Test

## Expected environment

- .NET SDK 10.x
- restore/build/test from the repository root

## Solution commands

```powershell
dotnet restore .\MCPServer.slnx
dotnet build .\MCPServer.slnx -c Debug
dotnet test .\MCPServer.slnx -c Debug
```

## Important notes

- Package versions are managed centrally in `Directory.Packages.props`.
- The repo uses `System.Text.Json` source generation and disables default reflection-based serialization in `Directory.Build.props`.
- Test projects are expected to restore through standard SDK-style package references.
- The Python bridge has its own sync/build step and should be validated from the published wheel when that surface changes.
- Workspace sandbox state is persisted in SQLite, so changes to the workspace layer should be exercised through both the workspace unit tests and the host-level smoke tests that attach over stdio and Streamable HTTP.

## Suggested verification order when debugging compile failures

If the whole solution fails, start with the lowest-level projects and move upward:

```powershell
dotnet build .\MCPServer.Domain\MCPServer.Domain.csproj
dotnet build .\MCPServer.Client\MCPServer.Client.csproj
dotnet build .\MCPServer.Application\MCPServer.Application.csproj
dotnet build .\MCPServer.Workspace\MCPServer.Workspace.csproj
dotnet build .\MCPServer.Tools.Workspace\MCPServer.Tools.Workspace.csproj
dotnet build .\MCPServer.Client.Infrastructure\MCPServer.Client.Infrastructure.csproj
dotnet build .\MCPServer.Infrastructure\MCPServer.Infrastructure.csproj
dotnet build .\MCPServer.Ssh\MCPServer.Ssh.csproj
dotnet build .\MCPServer.Tools.Ssh\MCPServer.Tools.Ssh.csproj
dotnet build .\MCPServer.AgentRouter.PythonBridge.Native\MCPServer.AgentRouter.PythonBridge.Native.csproj
dotnet build .\MCPServer.Host\MCPServer.Host.csproj
dotnet build .\MCPServer.Host.Sidecar\MCPServer.Host.Sidecar.csproj
dotnet build .\MCPServer.Client.Console\MCPServer.Client.Console.csproj
dotnet test .\MCPServer.UnitTests\MCPServer.UnitTests.csproj
dotnet test .\MCPServer.ProtocolTests\MCPServer.ProtocolTests.csproj
dotnet test .\MCPServer.AgentRouter.Tests\MCPServer.AgentRouter.Tests.csproj
dotnet test .\MCPServer.AgentRouter.IntegrationTests\MCPServer.AgentRouter.IntegrationTests.csproj
```

Fix the first real failure instead of chasing downstream metadata-file errors.

## Python bridge verification

When changes touch the NativeAOT bridge or the Python wrapper, validate the published bridge path too:

```powershell
pwsh .\scripts\Sync-PythonBridge.ps1 -BuildWheel
python -m unittest discover -s .\python\tests
```

If the bridge packaging changes, prefer running the smoke tests against the installed wheel from a clean directory as described in [docs/INSTALL.md](INSTALL.md).
