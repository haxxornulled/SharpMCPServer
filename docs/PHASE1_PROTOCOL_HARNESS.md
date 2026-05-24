# Phase 1 Protocol Harness

`MCPServer.ProtocolTests` contains transcript-style tests that exercise the parser, dispatcher, and serializer together. These tests are intentionally above unit-test level but below a full spawned-process integration test.

The harness validates:

- lifecycle ordering
- notification no-response behavior
- JSON-RPC response shape
- malformed JSON behavior
- invalid request ID behavior
- unknown request method behavior
- unknown notification behavior
- tool-list and tool-call behavior
- logging level changes
- stdio frame reader behavior for LF, CRLF, EOF-without-newline, and oversized frames

Run it directly:

```powershell
dotnet test .\MCPServer.ProtocolTests\MCPServer.ProtocolTests.csproj -c Release
```

Or run the whole solution:

```powershell
dotnet test .\MCPServer.slnx -c Release
```

The harness deliberately avoids starting the host process. That keeps failures precise and fast. A spawned-process stdio test can be added later once the host command-line surface is stable.
