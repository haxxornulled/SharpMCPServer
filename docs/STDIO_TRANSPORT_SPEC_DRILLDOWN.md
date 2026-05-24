# MCP 2025-11-25 stdio Transport Drilldown

Source: `https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#stdio`

This document tracks the project against the stdio-specific transport rules. This is intentionally narrow: it does not cover Streamable HTTP.

## stdio Contract

| Spec rule | Project decision | Code/tests |
|---|---|---|
| Client launches server as subprocess. | Host is an executable Generic Host intended to be launched by an MCP client. | `MCPServer.Host`; README run examples. |
| Server reads JSON-RPC from `stdin`. | `StdioMcpTransportSession` owns `Console.OpenStandardInput()` and reads byte frames directly. | `StdioMcpTransportSession.Open(...)`; `StdioFrameReaderTests`. |
| Server writes messages to `stdout`. | `JsonRpcResponseSerializer` writes only serialized JSON-RPC frames to the session output stream. | `JsonRpcResponseSerializer`; protocol transcript tests. |
| Messages are JSON-RPC requests, notifications, or responses. | Parser accepts object envelopes; dispatcher ignores client responses and handles server-side requests/notifications. | `JsonRpcMessageParser`; `McpRequestDispatcher`; transcript tests. |
| Messages are newline-delimited. | Input frames require `\n` delimiters by default. EOF with a partial unterminated frame is invalid unless a diagnostic option explicitly relaxes it. | `AllowFinalFrameWithoutNewline=false`; `ReadFrameAsync_Rejects_Final_Frame_When_Input_Ends_Without_Newline_By_Default`. |
| Messages MUST NOT contain embedded newlines. | Raw `\n` splits frames. Raw embedded `\r` is rejected after CRLF normalization. Output serialization scans for raw `\r`/`\n` before writing. Escaped JSON string line breaks remain `\\n`/`\\r`. | `ReadFrameAsync_Rejects_Embedded_Carriage_Returns`; `WriteNotificationAsync_Escapes_LineBreaks_And_Writes_Exactly_One_Frame_Delimiter`. |
| JSON-RPC messages MUST be UTF-8 encoded. | Parser operates on UTF-8 bytes and rejects invalid UTF-8 through `JsonDocument.Parse`. No intermediate line string is allocated in the transport. | `JsonRpcMessageParser.Parse(ReadOnlyMemory<byte>)`; `Invalid_Utf8_Frame_Returns_Parse_Error_Frame`. |
| Server MAY write UTF-8 strings to `stderr` for logs. | Serilog console sink is configured with `standardErrorFromLevel: Verbose`. | `Program.cs`; `STDIO_TROUBLESHOOTING.md`. |
| Client SHOULD NOT assume stderr means failure. | Docs tell developers stderr is diagnostic only in stdio mode. | `STDIO_TROUBLESHOOTING.md`. |
| Server MUST NOT write anything to stdout that is not a valid MCP message. | No `Console.WriteLine`; stdout is only accessed by `StdioMcpTransportSession.Output` and `JsonRpcResponseSerializer`. Output tests parse every frame as JSON-RPC. | `Output_Frames_Are_Newline_Delimited_Utf8_JsonRpc_Objects_With_No_Embedded_LineBreaks`; grep guard recommendation below. |
| Client MUST NOT write anything to server stdin that is not a valid MCP message. | Server responds deterministically to malformed JSON/UTF-8/ID frames and rejects invalid transport frames. Empty frames are not treated as MCP messages. | malformed JSON, invalid UTF-8, invalid ID, invalid frame tests. |

## Developer guardrails

Before a Phase 1 closeout PR is accepted, run:

```bash
dotnet test -c Release
```

And inspect stdout-writing risk:

```bash
grep -R "Console\.\|WriteLine" -n MCPServer.* --exclude-dir=bin --exclude-dir=obj
```

The only acceptable `Console.*` hits in stdio mode are opening standard streams in `StdioMcpTransportSession`. No banner, diagnostic, startup, or exception text may go to stdout.

## Strictness notes

- CRLF input is tolerated as a platform line-ending variant, but the final `\r` is stripped only when paired with the frame delimiter.
- A raw `\r` elsewhere is treated as an embedded newline violation.
- EOF with an incomplete frame is invalid by default because the spec says messages are newline-delimited.
- The serializer performs a cheap O(n) scan before stdout writes to prevent accidental raw line breaks from ever leaving the server.
- The transport remains sequential in Phase 1. If concurrent request processing is added later, stdout writes must stay serialized.


## Base protocol hardening tied to stdio

Stdio only frames bytes; the protocol layer still enforces MCP's JSON-RPC constraints after a frame is read:

- Request IDs are accepted only when they are strings or integer numbers. `null`, fractional numbers, booleans, objects, and arrays are rejected.
- Client request IDs are tracked for the life of the session and cannot be reused.
- `params`, when present, must be a JSON object.
- MCP `notifications/*` methods are one-way methods. If a client sends one with an `id`, it is rejected as an invalid request and does not advance lifecycle state.
