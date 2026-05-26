namespace MCPServer.Execution.Abstractions.Models;

public readonly record struct AgentPluginExecutionResult(
    bool Succeeded,
    string Status,
    string Message,
    string? ExternalRunId,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public static AgentPluginExecutionResult Success(
        string status,
        string message,
        string? externalRunId,
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        return new AgentPluginExecutionResult(
            Succeeded: true,
            Status: status,
            Message: message,
            ExternalRunId: externalRunId,
            Metadata: metadata ?? new Dictionary<string, string?>(capacity: 0));
    }
}
