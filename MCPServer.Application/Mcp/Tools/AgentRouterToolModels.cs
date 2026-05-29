using System.Text.Json.Serialization;

namespace MCPServer.Application.Mcp.Tools;

public sealed class AgentRunCreateRequest
{
    public string Objective { get; set; } = string.Empty;

    public string Capability { get; set; } = string.Empty;

    public string? WorkflowMode { get; set; }

    public string? RouteTarget { get; set; }

    public string? ParentRunId { get; set; }
}

public sealed class AgentRunTargetRequest
{
    public string RunId { get; set; } = string.Empty;
}

public sealed class AgentRunApproveRequest
{
    public string RunId { get; set; } = string.Empty;

    public string ApprovalId { get; set; } = string.Empty;

    public string? ApprovedBy { get; set; }
}

public sealed class AgentRunStructuredContent
{
    public string RunId { get; init; } = string.Empty;

    public string Objective { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public long Version { get; init; }

    public string Capability { get; init; } = string.Empty;

    public string WorkflowMode { get; init; } = string.Empty;

    public string RouteTarget { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string ParentRunId { get; init; } = string.Empty;

    public bool ApprovalGranted { get; init; }

    public string ApprovalId { get; init; } = string.Empty;

    public string ApprovedBy { get; init; } = string.Empty;

    public Dictionary<string, string> Metadata { get; init; } = [];
}
