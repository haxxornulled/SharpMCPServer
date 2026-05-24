# Performance Notes

This project is intended to be a high-throughput C# MCP server, not a demo server. Performance choices should preserve protocol correctness, avoid accidental allocations on the hot path, and stay maintainable under enterprise support pressure.

## Current Phase 1 Choices

- JSON uses built-in `System.Text.Json` only.
- Known MCP DTOs are registered in `McpJsonSerializerContext` for source-generated metadata.
- `JsonSerializerIsReflectionEnabledByDefault` is disabled in `Directory.Build.props` so accidental reflection-based JSON use fails early.
- JSON-RPC ID, message, response, and error envelopes are readonly structs.
- JSON-RPC responses are written manually with `Utf8JsonWriter` into a pooled `IBufferWriter<byte>` and then flushed to stdout as UTF-8.
- Tool descriptors are cached once during registry construction.
- Autofac keyed services are used for method/tool selection; the dispatcher does not scan handler collections per request.
- The stdio transport has a max input frame guard to reduce memory-abuse risk.
- Serilog writes to stderr only so stdout stays protocol-clean.
- Runtime switches enable tiered PGO and server GC.

## Performance Rules Going Forward

- Prefer source-generated `System.Text.Json` contracts over reflection-based serialization.
- Keep anonymous objects out of protocol responses.
- Use `JsonElement` only when raw JSON needs to be preserved across dispatch boundaries.
- Avoid LINQ in request hot paths.
- Do not introduce MediatR, Newtonsoft.Json, dynamic JSON, or reflection-heavy dispatch.
- Use structs for small protocol envelopes and identifiers; do not use structs for large mutable models or descriptor graphs where copying would hurt more than help.
- Keep protocol errors separate from tool execution errors.
- Add benchmark projects only after the protocol behavior is covered by tests.

## Fin and Disposal Rules

- Use `LanguageExt.Fin<T>` for expected recoverable failures inside the protocol/application boundary instead of ad-hoc `Result<T>` types or exception-only control flow.
- Keep JSON-RPC protocol failures distinct from infrastructure failures: a malformed request should become a JSON-RPC error response, while a failed internal operation should remain a `Fin<T>` failure until the transport boundary maps it.
- Tool execution can still return a successful MCP `ToolCallResult` with `isError: true`; that is a protocol result, not a dispatcher failure.
- Use `IDisposable` / `IAsyncDisposable` at ownership boundaries. The stdio transport session now owns stdin/stdout plus rented buffers explicitly and is consumed with `await using`.
- Avoid fake disposal on stateless services; only implement disposal when the type actually owns resources or participates in a resource-lifetime contract.

## Fin Construction Rule

- Do not rely on implicit conversion from successful values or `Error` into `Fin<T>`. Use `Fin.Succ<T>(value)` and `Fin.Fail<T>(error)` explicitly so the code stays compatible with the LanguageExt v5 line and compiler failures are obvious at the call site. Do not use `using static LanguageExt.Prelude`; it pollutes protocol code with helper names such as `Array<T>()` and can shadow BCL types.

- Tool descriptors are cached once by `McpToolRegistry` in Autofac registration/enumeration order. Do not sort at runtime unless the MCP spec requires ordering or a measured client compatibility issue justifies it.

## Buffering and pooling rules

- Hot stdio paths must not use `StreamReader.ReadLineAsync()` because it allocates a managed `string` for every MCP frame.
- Stdio input frames are read as UTF-8 bytes into `ArrayPool<byte>` buffers and parsed directly from `ReadOnlyMemory<byte>`.
- Response serialization uses `Utf8JsonWriter` over a project-owned pooled `IBufferWriter<byte>` instead of `ArrayBufferWriter<byte>` per response.
- Rented input buffers are returned with clearing enabled by default because MCP tool parameters may contain secrets. Tune `StdioMcpTransportOptions.ClearReturnedInputBuffers` only after profiling and reviewing the threat model.
- Do not return rented buffers more than once. Ownership transfer must be explicit through `PooledByteBuffer`.
- Do not hold pooled input memory beyond JSON parsing. Clone any `JsonElement` values that must survive the rented-buffer lifetime.


## Phase 1 continuation rules

- Cancellation support uses request IDs as stable raw JSON keys so numeric and string IDs do not collide.
- Request timeout behavior is configurable through `McpRequestExecutionOptions`; pings are excluded from default timeout timers.
- `tools/list` keeps the no-cursor single-page fast path cached and only allocates page arrays when cursor paging is actually requested or needed.
- Tool-name validation is implemented without regular expressions to avoid startup overhead and hidden allocations.
- JSON-RPC notification emission uses the same pooled writer path as responses.
- xUnit v3 is used for tests; v3 projects are executable, so the test project explicitly uses `OutputType=Exe`.


## Phase 1 validation and output guards

- Tool argument validation is implemented with `System.Text.Json` and a small in-process validator instead of pulling in Newtonsoft-backed schema tooling.
- The validator intentionally covers the subset needed by MCP tool input schemas in this server: primitive/object/array `type`, `required`, `properties`, `additionalProperties`, `items`, `enum`, basic length/count limits, and numeric bounds.
- `tools/call` argument validation failures return a normal MCP tool result with `isError=true`; malformed `tools/call` request envelopes still return JSON-RPC `InvalidParams`.
- JSON-RPC response serialization now has a configured maximum output frame size. The serializer writes into rented buffers and refuses to flush oversized frames to stdout.

## C# 14 pattern matching / nullable flow

Use C# 14 pattern matching to bind non-null values and avoid nullable suppressions. Avoid `default!` in application code and tests. Prefer patterns such as `{ IsSuccess: true, Tool: { } tool }`, tuple switches for lifecycle state, and relational patterns for tight validators.
