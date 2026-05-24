# AgentRouter Approval Policy

The AgentRouter owns the control-plane approval gate before a plugin can execute a high-risk capability.

## Responsibility split

- Domain owns capability approval rules and policy decisions.
- Application evaluates plugin execution requests against the domain policy before invoking a plugin.
- Plugins expose capabilities and risk metadata but do not decide whether they are allowed to run.
- Infrastructure and SSH adapters perform execution only after the AgentRouter policy gate allows it.

## Metadata

Generic approval metadata:

```text
agent.capability
agent.approval.granted
agent.approval.id
```

`agent.approval.granted=true` is the current explicit approval marker. This is intentionally simple for the first slice. A later slice should replace the metadata-only marker with an approval store or signed approval token.

## Current behavior

For a capability that does not require approval:

```text
queued -> planning -> working -> completed/failed
```

For a capability that requires approval and has no approval marker:

```text
queued -> planning -> awaiting_approval
```

The plugin is not executed in this state.

For a capability that requires approval and is explicitly approved:

```text
queued -> planning -> working -> completed/failed
```

## SSH implication

The SSH plugin exposes `remote-shell` and `ssh-agent` as critical capabilities that require approval. AgentRouter core remains SSH-agnostic; it only sees capability descriptors and policy decisions.
