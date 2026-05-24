# stdio Troubleshooting

## Client fails to parse the first message

Check that the server is not writing logs, banners, startup text, or exception dumps to stdout. In stdio mode, stdout is the JSON-RPC protocol stream. Serilog is configured to write to stderr for this reason.

## `tools/list` fails before initialization

The expected startup sequence is:

1. Client sends `initialize`.
2. Server responds with protocol version, capabilities, and server info.
3. Client sends `notifications/initialized`.
4. Client sends normal requests such as `tools/list` or `tools/call`.

Phase 1 intentionally blocks normal requests before this sequence completes.

## Tool call returns `isError=true`

That means the JSON-RPC envelope was valid and the server reached the tool boundary, but the tool call failed logically. Common causes include an unknown tool name or arguments that do not match the tool input schema.

## JSON-RPC protocol error response

A response with an `error` property means the protocol request was malformed or invalid for the current session state. Examples: wrong JSON-RPC version, missing method, unsupported method, invalid request ID shape, or calling methods before initialization.

## Process hangs while reading

The stdio transport expects newline-delimited UTF-8 JSON-RPC frames. A client must terminate each JSON object with `\n`.

## Large messages fail

Input frames and serialized output frames have configured size guards. Increase `StdioMcpTransportOptions.MaxInputFrameBytes` or `JsonRpcSerializationOptions.MaxOutputFrameBytes` only after checking that the tool producing the data really should be allowed to do so.


## Embedded newline or unterminated frame

The stdio transport is strict by default. Every incoming JSON-RPC message must end with `\n`. CRLF is accepted and normalized, but raw carriage returns inside a frame are rejected because stdio messages must not contain embedded newlines. EOF with a partial frame is rejected unless `StdioMcpTransportOptions.AllowFinalFrameWithoutNewline` is enabled for diagnostics.

## Invalid UTF-8

MCP JSON-RPC messages must be UTF-8 encoded. The parser consumes UTF-8 bytes directly and returns a parse error for invalid byte sequences.
