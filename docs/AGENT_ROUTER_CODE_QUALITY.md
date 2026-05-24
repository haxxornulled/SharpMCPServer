# AgentRouter code quality guardrails

This project targets .NET 10 / C# 14, but modern language features should be used to make the code safer and clearer, not to make it clever.

## Rules

- Do not put `ref`, `in`, or `out` parameters on `async` methods.
- Keep `in` parameters only on non-async public entry points when avoiding struct copies is useful.
- Prefer rich domain methods over direct snapshot mutation.
- Prefer pattern matching for state-machine validation and request validation.
- Keep hosted-service lifecycle and queue supervision in the hosting/application layers.
- Keep plugin mechanics outside the AgentRouter core.
- Keep MCPServer-specific concepts out of AgentRouter contracts.

## Workflow split

Deterministic workflow:

```text
validate -> approve -> execute -> collect output -> complete
```

Agentic workflow:

```text
objective -> plan -> select capability -> execute -> inspect -> continue/stop
```

The deterministic path should remain explicit and testable. Agentic behavior can be layered on top later without weakening the state machine or approval/concurrency rules.
