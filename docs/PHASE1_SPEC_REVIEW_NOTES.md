# Phase 1 Spec Review Notes

Reviewed against MCP 2025-11-25 Base Protocol, Lifecycle, stdio Transport, Progress, Cancellation, Logging, Tools, and Schema Reference.

## Findings fixed in this pass

1. `ping` request parameters were too strict.
   - The schema allows `params?: RequestParams` for `ping`, so an object-shaped params payload such as `_meta.progressToken` must not be rejected.
   - Fixed `PingHandler` to accept absent params or object params.

2. Invalid MCP notification methods with an `id` were registering the ID before rejection.
   - Notifications must not include an ID. Invalid notification frames should not consume a request ID for the session.
   - Moved duplicate request ID registration until after the notification-with-ID guard.

3. Tool descriptor validation was too strict on `description` and not strict enough on schema root type.
   - `Tool.description` is optional.
   - `Tool.inputSchema` and `Tool.outputSchema`, when present, are restricted to root `type: "object"`.
   - Removed the mandatory description check and added root object-type validation for input/output schemas.

## Follow-up decision applied

MCP 2025-11-25 represents `JSONRPCErrorResponse.id` as optional `RequestId` and says error responses include the same ID except when the ID could not be read due to a malformed request. The implementation now omits the `id` property for parse errors or invalid ID errors where no valid MCP request ID can be read. It still includes the original string/integer ID for valid requests that fail after the ID is known.

## Scope still intentionally out of Phase 1

- Streamable HTTP transport
- HTTP authorization
- Prompts/resources/completions
- Client-side roots/sampling/elicitation requests
- Task-augmented execution
- MCP log forwarding from application `ILogger` to `notifications/message`

## Parser optional `params` fix

`params` is optional in MCP request and notification messages. The parser must preserve the difference between an absent `params` property and a present non-object `params` value. A C# conditional expression accidentally materialized an absent `params` property as `default(JsonElement)`, which made `HasParams` true and caused valid no-param requests like `ping` and `tools/list` to fail before reaching duplicate request-id validation. This has been replaced with an explicit `if (TryGetProperty(...))` assignment, and parser tests now cover both absent and present object params.
