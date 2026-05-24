# MCP 2025-11-25 Completion Notes

This project now implements the server-side `completion/complete` utility for Phase 1.

## Implemented

- `initialize` advertises:

```json
"completions": {}
```

- `completion/complete` validates:
  - `params` exists and is an object.
  - `ref` is an object.
  - `ref.type` is either `ref/prompt` or `ref/resource`.
  - prompt references include a non-empty `name`.
  - resource references include a non-empty `uri`.
  - `argument.name` and `argument.value` are strings.
  - `context.arguments`, when supplied, is an object whose values are strings.

- Completion results enforce the MCP maximum of 100 returned values.
- Built-in prompt completion is available for `server.status` argument `focus`.
- Resource-template completion currently returns an empty completion result because Phase 1 has no registered resource templates.

## Design Notes

Completion is modelled as a server utility, not as part of prompts or resources directly. The handler validates the common MCP request shape and then delegates to a prompt-specific completion provider when the referenced prompt supports completions.

The response shape is always:

```json
{
  "completion": {
    "values": [],
    "total": 0,
    "hasMore": false
  }
}
```

Values are intentionally plain strings and never include hidden server state.
