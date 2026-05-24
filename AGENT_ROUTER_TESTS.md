# AgentRouter Package Tests

AgentRouter package tests are isolated in:

```text
MCPServer.AgentRouter.Defaults.Tests
```

They reference only:

```text
MCPServer.AgentRouter.Abstractions
MCPServer.AgentRouter.Defaults
```

They do not reference:

```text
MCPServer.Application
MCPServer.Infrastructure
MCPServer.Tools.Ssh
MCPServer.Host
MCPServer.UnitTests
```

This keeps the default provider package independently testable. Third parties can implement their own router package against `MCPServer.AgentRouter.Abstractions` without coupling to this server.

Run AgentRouter package tests using the xUnit v3 test executable style:

```powershell
dotnet run --project MCPServer.AgentRouter.Defaults.Tests/MCPServer.AgentRouter.Defaults.Tests.csproj
```

Build the solution:

```powershell
dotnet build MCPServer.slnx
```

Do not run these AgentRouter package tests through the old `MCPServer.UnitTests` harness.
