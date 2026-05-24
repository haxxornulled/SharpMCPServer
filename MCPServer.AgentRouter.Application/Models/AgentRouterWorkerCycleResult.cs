namespace MCPServer.AgentRouter.Application.Models;

public readonly record struct AgentRouterWorkerCycleResult(
    int ProcessedCount,
    string? Message)
{
    public static AgentRouterWorkerCycleResult Idle(string? message = null)
    {
        return new AgentRouterWorkerCycleResult(
            ProcessedCount: 0,
            Message: message ?? "No AgentRouter work was available.");
    }

    public static AgentRouterWorkerCycleResult Processed(
        int processedCount,
        string? message = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(processedCount);

        return new AgentRouterWorkerCycleResult(
            ProcessedCount: processedCount,
            Message: message);
    }
}
