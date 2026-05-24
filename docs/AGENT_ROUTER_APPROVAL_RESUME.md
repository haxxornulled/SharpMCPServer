# AgentRouter Approval Resume Slice

This slice closes the first approval loop without introducing an approval database or UI.

## Flow

```text
queued
  -> planning
  -> awaiting_approval
  -> ApproveAsync
  -> queued
  -> planning
  -> working
  -> completed / failed
```

## Boundary

AgentRouter owns the approval state transition and re-queue operation. Plugins still own execution mechanics only.

The approval use case is exposed through `IAgentRunCoordinator.ApproveAsync`. It loads the persisted run snapshot, validates that the run is currently `awaiting_approval`, delegates the approval transition to the `AgentRun` aggregate, persists/traces the approved snapshot, and re-enqueues the run through `IAgentRunQueue` when a queue is registered.

## Metadata

Run snapshots now carry provider-neutral metadata so an approved run can be re-enqueued with the original capability request plus approval markers.

Required approval marker:

```text
agent.approval.granted=true
agent.approval.id=<approval id>
```

Optional marker:

```text
agent.approval.approvedBy=<actor>
```

This is still intentionally lightweight. A later slice can replace metadata approval markers with a durable approval store or signed approval token without changing plugin execution semantics.
