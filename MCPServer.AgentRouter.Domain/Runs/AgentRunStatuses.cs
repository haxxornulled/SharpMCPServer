namespace MCPServer.AgentRouter.Domain.Runs;

public static class AgentRunStatuses
{
    public const string Queued = "queued";
    public const string Planning = "planning";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Working = "working";
    public const string InputRequired = "input_required";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
