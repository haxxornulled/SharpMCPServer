namespace MCPServer.AgentRouter.Domain.Runs;

public readonly record struct AgentExecutionLease(
    string OwnerId,
    DateTimeOffset AcquiredAtUtc,
    DateTimeOffset ExpiresAtUtc)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(OwnerId);

    public bool IsExpiredAt(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    public bool IsOwnedBy(string ownerId)
    {
        return !string.IsNullOrWhiteSpace(ownerId)
            && string.Equals(OwnerId, ownerId.Trim(), StringComparison.Ordinal);
    }

    public static AgentExecutionLease Acquire(
        string ownerId,
        DateTimeOffset nowUtc,
        TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "Agent execution lease duration must be greater than zero.");
        }

        return new AgentExecutionLease(
            ownerId.Trim(),
            nowUtc,
            nowUtc.Add(duration));
    }
}
