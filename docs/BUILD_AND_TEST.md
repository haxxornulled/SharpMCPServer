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

## Suggested verification order when debugging compile failures

If the whole solution fails, start with the lowest-level projects and move upward:

```powershell
dotnet build .\MCPServer.Domain\MCPServer.Domain.csproj
dotnet build .\MCPServer.Application\MCPServer.Application.csproj
dotnet build .\MCPServer.Infrastructure\MCPServer.Infrastructure.csproj
dotnet build .\MCPServer.Ssh\MCPServer.Ssh.csproj
dotnet build .\MCPServer.Tools.Ssh\MCPServer.Tools.Ssh.csproj
dotnet build .\MCPServer.Host\MCPServer.Host.csproj
dotnet test .\MCPServer.UnitTests\MCPServer.UnitTests.csproj
dotnet test .\MCPServer.ProtocolTests\MCPServer.ProtocolTests.csproj
dotnet test .\MCPServer.AgentRouter.Tests\MCPServer.AgentRouter.Tests.csproj
dotnet test .\MCPServer.AgentRouter.IntegrationTests\MCPServer.AgentRouter.IntegrationTests.csproj
```

Fix the first real failure instead of chasing downstream metadata-file errors.
