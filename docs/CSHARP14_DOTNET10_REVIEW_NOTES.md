# C# 14 / .NET 10 Review Notes

This pass pins the repo to C# 14 and tightens nullable control flow using pattern matching rather than suppressions.

## Decisions

- `Directory.Build.props` pins `<LangVersion>14.0</LangVersion>` for deterministic compiler behavior on .NET 10.
- Hot protocol control flow should bind non-null locals through pattern matching, for example `message.Method is not { } method` and tuple/property patterns.
- Do not use `default!` to silence nullable analysis.
- Use nullable result fields plus property patterns such as `{ IsSuccess: true, Tool: { } tool }` when projecting `Fin<T>` into small local outcome structs.
- Prefer property patterns over repeated `.Value` access on `JsonElement?`.
- Keep allocation-sensitive work on `System.Text.Json`, UTF-8 literals, pooled buffers, and source-generated metadata.

## Applied cleanup

- Replaced nullable warning suppressions in handlers/tests.
- Reworked `McpRequestDispatcher` lifecycle gating as a tuple-pattern switch.
- Reworked tool lookup/execution outcome structs to use nullable payload fields plus pattern matching.
- Reworked parser shape checks with property patterns.
- Made `Tool.description` optional in the domain model to match the MCP schema behavior already expected by tests.
- Reworked tool-name character validation using relational and constant patterns.

## Nullable cleanup follow-up

The C# 14 pattern pass now avoids negative-pattern variable capture where nullable flow analysis cannot prove assignment. For example, `ToolsListHandler` now reads into a local first, then applies a property-pattern success check.

Transient result structs that carry an error now use `Error?` for the failure-only field. Success paths no longer pass `default` into non-nullable `Error` constructor parameters, which keeps nullable analysis strict without using `default!`.

Project rule: use property patterns to unwrap optional values, and only make fields nullable when the value is genuinely absent in one branch of the result shape.
