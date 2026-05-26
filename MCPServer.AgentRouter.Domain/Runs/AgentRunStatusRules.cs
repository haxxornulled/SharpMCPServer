namespace MCPServer.AgentRouter.Domain.Runs;

public static class AgentRunStatusRules
{
    public static bool IsTerminal(string? status)
    {
        return status is AgentRunStatuses.Completed
            or AgentRunStatuses.Failed
            or AgentRunStatuses.Cancelled;
    }

    public static bool CanTransition(string? currentStatus, string nextStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nextStatus);

        return currentStatus switch
        {
            null or "" => nextStatus is AgentRunStatuses.Queued,

            _ when IsTerminal(currentStatus) => false,

            _ when string.Equals(currentStatus, nextStatus, StringComparison.Ordinal) => true,

            AgentRunStatuses.Queued => nextStatus is AgentRunStatuses.Planning
                or AgentRunStatuses.Cancelled
                or AgentRunStatuses.Failed,

            AgentRunStatuses.Planning => nextStatus is AgentRunStatuses.AwaitingApproval
                or AgentRunStatuses.Working
                or AgentRunStatuses.InputRequired
                or AgentRunStatuses.Cancelled
                or AgentRunStatuses.Failed,

            AgentRunStatuses.AwaitingApproval => nextStatus is AgentRunStatuses.Queued
                or AgentRunStatuses.Working
                or AgentRunStatuses.Cancelled
                or AgentRunStatuses.Failed,

            AgentRunStatuses.Working => nextStatus is AgentRunStatuses.InputRequired
                or AgentRunStatuses.AwaitingApproval
                or AgentRunStatuses.Completed
                or AgentRunStatuses.Cancelled
                or AgentRunStatuses.Failed,

            AgentRunStatuses.InputRequired => nextStatus is AgentRunStatuses.Planning
                or AgentRunStatuses.Working
                or AgentRunStatuses.Cancelled
                or AgentRunStatuses.Failed,

            _ => false
        };
    }
}
