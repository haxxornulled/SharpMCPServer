namespace MCPServer.AgentRouter.Application.Options;

public sealed class AgentRouterConcurrencyOptions
{
    public static AgentRouterConcurrencyOptions Default { get; } = new();

    public int MaxQueuedRuns { get; init; } = 100;

    public int MaxConcurrentRuns { get; init; } = 1;

    public int MaxConcurrentStepsPerRun { get; init; } = 1;

    public string QueueFullMode { get; init; } = AgentRunQueueFullModes.Wait;

    public TimeSpan StopDrainTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ExecutionLeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxQueuedRuns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentRuns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentStepsPerRun, 1);

        if (!AgentRunQueueFullModes.IsDefined(QueueFullMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(QueueFullMode),
                QueueFullMode,
                "AgentRouter queue full mode must be either 'wait' or 'reject'.");
        }

        if (StopDrainTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StopDrainTimeout),
                "AgentRouter stop drain timeout cannot be negative.");
        }

        if (ExecutionLeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExecutionLeaseDuration),
                "AgentRouter execution lease duration must be greater than zero.");
        }
    }
}
