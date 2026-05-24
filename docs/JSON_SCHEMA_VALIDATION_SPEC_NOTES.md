# JSON Schema Validation Notes

MCP 2025-11-25 uses JSON Schema for embedded tool schemas. The project now validates tool input and output schemas through an Infrastructure adapter backed by `JsonSchema.Net`.

Rules applied by the project:

- Schemas must be JSON objects.
- Tool `inputSchema` and `outputSchema` roots must declare `type: "object"`, matching the MCP schema restriction.
- Missing `$schema` is treated as JSON Schema 2020-12.
- Explicit `$schema` is currently accepted only for `https://json-schema.org/draft/2020-12/schema`.
- Tool schemas are built and validated at registration time.
- Tool call arguments are validated before tool execution.
- Tool `structuredContent` is validated when the tool descriptor declares an `outputSchema`.

Architecture decision:

- `IMcpToolArgumentValidator` remains an Application-layer port.
- `JsonSchemaNetToolArgumentValidator` is an Infrastructure adapter.
- `JsonSchema.Net` is referenced only by Infrastructure, not by Application or Domain.

Performance decision:

- Compiled `JsonSchema` instances are cached by raw schema text.
- Validation uses `JsonElement` directly.
- Detailed list output is enabled for actionable errors; this is acceptable because schema validation is at the protocol boundary, not on the JSON-RPC frame parsing path.
