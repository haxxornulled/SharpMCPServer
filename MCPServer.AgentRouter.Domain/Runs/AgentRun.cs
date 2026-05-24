using MCPServer.AgentRouter.Domain.Objectives;

namespace MCPServer.AgentRouter.Domain.Runs;

public sealed class AgentRun
{
    private const string ApprovalGrantedKey = "agent.approval.granted";
    private const string ApprovalIdKey = "agent.approval.id";
    private const string DefaultApprovedMessage = "AgentRouter run approved and re-queued.";

    private AgentRun(
        AgentRunId runId,
        AgentObjective objective,
        string status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        string? message,
        long version,
        IReadOnlyDictionary<string, string?>? metadata)
    {
        if (runId.IsEmpty)
        {
            throw new ArgumentException("Agent run id is required.", nameof(runId));
        }

        if (objective.IsEmpty)
        {
            throw new ArgumentException("Agent objective is required.", nameof(objective));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updatedAtUtc),
                "Updated timestamp cannot be earlier than created timestamp.");
        }

        RunId = runId;
        Objective = objective;
        Status = status;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        Message = message;
        Version = version;
        Metadata = CopyMetadata(metadata);
    }

    public AgentRunId RunId { get; }

    public AgentObjective Objective { get; }

    public string Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public string? Message { get; private set; }

    public long Version { get; private set; }

    public IReadOnlyDictionary<string, string?> Metadata { get; private set; }

    public bool IsTerminal => AgentRunStatusRules.IsTerminal(Status);

    public static AgentRun Queue(
        AgentRunId runId,
        AgentObjective objective,
        DateTimeOffset nowUtc,
        string? message,
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        return new AgentRun(
            runId,
            objective,
            AgentRunStatuses.Queued,
            nowUtc,
            nowUtc,
            message,
            version: 0,
            metadata);
    }

    public static AgentRun Rehydrate(in AgentRunSnapshot snapshot)
    {
        return new AgentRun(
            snapshot.RunId,
            snapshot.Objective,
            snapshot.Status,
            snapshot.CreatedAtUtc,
            snapshot.UpdatedAtUtc,
            snapshot.Message,
            snapshot.Version,
            snapshot.MetadataOrEmpty);
    }

    public AgentRunSnapshot ToSnapshot()
    {
        return new AgentRunSnapshot(
            RunId,
            Objective,
            Status,
            CreatedAtUtc,
            UpdatedAtUtc,
            Message,
            Version,
            Metadata);
    }

    public AgentRunTransitionResult MarkPlanning(DateTimeOffset nowUtc, string? message)
    {
        return TransitionTo(AgentRunStatuses.Planning, nowUtc, message);
    }

    public AgentRunTransitionResult MarkAwaitingApproval(DateTimeOffset nowUtc, string? message)
    {
        return TransitionTo(AgentRunStatuses.AwaitingApproval, nowUtc, message);
    }

    public AgentRunTransitionResult MarkWorking(DateTimeOffset nowUtc, string? message)
    {
        return TransitionTo(AgentRunStatuses.Working, nowUtc, message);
    }

    public AgentRunTransitionResult MarkInputRequired(DateTimeOffset nowUtc, string? message)
    {
        return TransitionTo(AgentRunStatuses.InputRequired, nowUtc, message);
    }

    public AgentRunTransitionResult Complete(DateTimeOffset nowUtc, string? message)
    {
        return TransitionTo(AgentRunStatuses.Completed, nowUtc, message);
    }

    public AgentRunTransitionResult Fail(DateTimeOffset nowUtc, string? message)
    {
        return TransitionTo(AgentRunStatuses.Failed, nowUtc, message);
    }

    public AgentRunTransitionResult Cancel(DateTimeOffset nowUtc, string? message)
    {
        return TransitionTo(AgentRunStatuses.Cancelled, nowUtc, message);
    }

    public AgentRunTransitionResult Approve(
        DateTimeOffset nowUtc,
        string approvalId,
        IReadOnlyDictionary<string, string?>? metadata)
    {
        return Approve(nowUtc, approvalId, metadata, DefaultApprovedMessage);
    }

    public AgentRunTransitionResult Approve(
        DateTimeOffset nowUtc,
        string approvalId,
        IReadOnlyDictionary<string, string?>? metadata,
        string? message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);

        var transition = TransitionTo(AgentRunStatuses.Queued, nowUtc, message);
        if (!transition.Succeeded)
        {
            return transition;
        }

        Metadata = MergeApprovalMetadata(Metadata, metadata, approvalId);
        return transition;
    }

    private AgentRunTransitionResult TransitionTo(
        string nextStatus,
        DateTimeOffset nowUtc,
        string? message)
    {
        return (nowUtc < CreatedAtUtc, IsTerminal, AgentRunStatusRules.CanTransition(Status, nextStatus)) switch
        {
            (true, _, _) => AgentRunTransitionResult.Rejected(
                AgentRunTransitionErrorCodes.InvalidTransition,
                "Agent run transition timestamp cannot be earlier than the run creation timestamp."),

            (_, true, _) => AgentRunTransitionResult.Rejected(
                AgentRunTransitionErrorCodes.TerminalState,
                $"Agent run '{RunId.Value}' is already terminal with status '{Status}'."),

            (_, _, false) => AgentRunTransitionResult.Rejected(
                AgentRunTransitionErrorCodes.InvalidTransition,
                $"Agent run cannot transition from '{Status}' to '{nextStatus}'."),

            _ => ApplyTransition(nextStatus, nowUtc, message)
        };
    }

    private AgentRunTransitionResult ApplyTransition(
        string nextStatus,
        DateTimeOffset nowUtc,
        string? message)
    {
        Status = nextStatus;
        UpdatedAtUtc = nowUtc;
        Message = message;
        Version++;

        return AgentRunTransitionResult.Success();
    }

    private static Dictionary<string, string?> MergeApprovalMetadata(
        IReadOnlyDictionary<string, string?> current,
        IReadOnlyDictionary<string, string?>? additional,
        string approvalId)
    {
        var merged = CopyMetadata(current);

        if (additional is { Count: > 0 })
        {
            foreach (var item in additional)
            {
                merged[item.Key] = item.Value;
            }
        }

        merged[ApprovalGrantedKey] = "true";
        merged[ApprovalIdKey] = approvalId.Trim();

        return merged;
    }

    private static Dictionary<string, string?> CopyMetadata(IReadOnlyDictionary<string, string?>? metadata)
    {
        if (metadata is not { Count: > 0 })
        {
            return new Dictionary<string, string?>(capacity: 0, StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string?>(metadata, StringComparer.OrdinalIgnoreCase);
    }
}
