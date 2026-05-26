using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Domain.Runs;

namespace MCPServer.AgentRouter.Application.Models;

public readonly record struct AgentRouterApproveRunRequest(
    AgentRunId RunId,
    string ApprovalId,
    string? ApprovedBy,
    IReadOnlyDictionary<string, string?>? Metadata)
{
    public IReadOnlyDictionary<string, string?> MetadataOrEmpty => Metadata ?? AgentRouterMetadata.Empty;
}
