using System.Text.Json;
using LanguageExt;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.AgentRouter.Domain.Workflows;

namespace MCPServer.Application.Mcp.Tools;

internal static class AgentToolHelpers
{
    public static Fin<Unit> RequireWorkflowMode(string? workflowMode, string toolName)
    {
        if (string.IsNullOrWhiteSpace(workflowMode))
        {
            return Fin.Succ(Unit.Default);
        }

        return AgentWorkflowMode.TryCreate(workflowMode, out _)
            ? Fin.Succ(Unit.Default)
            : Fin.Fail<Unit>(LanguageExt.Common.Error.New($"{toolName} requires workflowMode to be either 'deterministic' or 'agentic'."));
    }

    public static Fin<Unit> RequireRouteTarget(string? routeTarget, string toolName)
    {
        if (string.IsNullOrWhiteSpace(routeTarget))
        {
            return Fin.Succ(Unit.Default);
        }

        return NormalizeRouteTarget(routeTarget) is { } normalized
            ? Fin.Succ(Unit.Default)
            : Fin.Fail<Unit>(LanguageExt.Common.Error.New($"{toolName} requires routeTarget to be one of 'local', 'local-model', 'remote', 'remote-api', 'mcp', or 'mcp-server'."));
    }

    public static Fin<AgentRouterStartRunRequest> CreateStartRequest(AgentRunCreateRequest request, bool isSubagent, string toolName)
    {
        if (AgentObjective.TryCreate(request.Objective, out var objective) is false)
        {
            return Fin.Fail<AgentRouterStartRunRequest>(LanguageExt.Common.Error.New($"{toolName} requires a non-empty objective."));
        }

        if (string.IsNullOrWhiteSpace(request.Capability))
        {
            return Fin.Fail<AgentRouterStartRunRequest>(LanguageExt.Common.Error.New($"{toolName} requires a non-empty capability."));
        }

        if (RequireWorkflowMode(request.WorkflowMode, toolName).IsFail)
        {
            return Fin.Fail<AgentRouterStartRunRequest>(LanguageExt.Common.Error.New($"{toolName} requires workflowMode to be either 'deterministic' or 'agentic'."));
        }

        if (RequireRouteTarget(request.RouteTarget, toolName).IsFail)
        {
            return Fin.Fail<AgentRouterStartRunRequest>(LanguageExt.Common.Error.New($"{toolName} requires routeTarget to be one of 'local', 'local-model', 'remote', 'remote-api', 'mcp', or 'mcp-server'."));
        }

        if (isSubagent && string.IsNullOrWhiteSpace(request.ParentRunId))
        {
            return Fin.Fail<AgentRouterStartRunRequest>(LanguageExt.Common.Error.New($"{toolName} requires a non-empty parentRunId."));
        }

        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [AgentRouterMetadataKeys.Capability] = request.Capability.Trim(),
            [AgentRouterMetadataKeys.Kind] = isSubagent ? "subagent" : "agent"
        };

        if (!string.IsNullOrWhiteSpace(request.WorkflowMode) && AgentWorkflowMode.TryCreate(request.WorkflowMode, out var workflowMode))
        {
            metadata[AgentRouterMetadataKeys.WorkflowMode] = workflowMode.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.RouteTarget))
        {
            metadata[AgentRouterMetadataKeys.RouteTarget] = NormalizeRouteTarget(request.RouteTarget)!;
        }

        if (isSubagent)
        {
            metadata[AgentRouterMetadataKeys.ParentRunId] = request.ParentRunId!.Trim();
        }

        return Fin.Succ(new AgentRouterStartRunRequest(objective, metadata));
    }

    public static AgentRunStructuredContent BuildStructuredContent(AgentRunSnapshot snapshot)
    {
        var metadata = BuildPublicMetadata(snapshot.MetadataOrEmpty);
        var capability = ResolveMetadataValue(snapshot.MetadataOrEmpty, AgentRouterMetadataKeys.Capability)
            ?? ResolveMetadataValue(snapshot.MetadataOrEmpty, "capability")
            ?? ResolveMetadataValue(snapshot.MetadataOrEmpty, "agentCapability")
            ?? string.Empty;
        var workflowMode = ResolveMetadataValue(snapshot.MetadataOrEmpty, AgentRouterMetadataKeys.WorkflowMode) ?? string.Empty;
        var routeTarget = ResolveMetadataValue(snapshot.MetadataOrEmpty, AgentRouterMetadataKeys.RouteTarget) ?? string.Empty;
        var parentRunId = ResolveMetadataValue(snapshot.MetadataOrEmpty, AgentRouterMetadataKeys.ParentRunId) ?? string.Empty;
        var kind = ResolveMetadataValue(snapshot.MetadataOrEmpty, AgentRouterMetadataKeys.Kind)
            ?? (!string.IsNullOrWhiteSpace(parentRunId) ? "subagent" : "agent");
        var approvalGranted = ResolveBoolean(snapshot.MetadataOrEmpty, AgentRouterMetadataKeys.ApprovalGranted)
            ?? ResolveBoolean(snapshot.MetadataOrEmpty, "approval.granted")
            ?? ResolveBoolean(snapshot.MetadataOrEmpty, "approved")
            ?? !string.IsNullOrWhiteSpace(ResolveMetadataValue(snapshot.MetadataOrEmpty, AgentRouterMetadataKeys.ApprovalId));

        return new AgentRunStructuredContent
        {
            RunId = snapshot.RunId.Value,
            Objective = snapshot.Objective.Value,
            Status = snapshot.Status,
            Message = snapshot.Message?.Trim() ?? string.Empty,
            CreatedAtUtc = snapshot.CreatedAtUtc,
            UpdatedAtUtc = snapshot.UpdatedAtUtc,
            Version = snapshot.Version,
            Capability = capability,
            WorkflowMode = workflowMode,
            RouteTarget = routeTarget,
            Kind = kind,
            ParentRunId = parentRunId,
            ApprovalGranted = approvalGranted,
            ApprovalId = ResolveMetadataValue(snapshot.MetadataOrEmpty, AgentRouterMetadataKeys.ApprovalId) ?? string.Empty,
            ApprovedBy = ResolveMetadataValue(snapshot.MetadataOrEmpty, AgentRouterMetadataKeys.ApprovalApprovedBy) ?? string.Empty,
            Metadata = metadata
        };
    }

    public static ValueTask<Fin<AgentRunSnapshot>> GetSnapshotAsync(
        IAgentRunCoordinator coordinator,
        AgentRunId runId,
        string toolName,
        CancellationToken cancellationToken)
    {
        if (runId.IsEmpty)
        {
            return new ValueTask<Fin<AgentRunSnapshot>>(Fin.Fail<AgentRunSnapshot>(LanguageExt.Common.Error.New($"{toolName} requires a non-empty runId.")));
        }

        return coordinator.GetSnapshotAsync(runId, cancellationToken);
    }

    public static Fin<AgentRunId> ParseRunId(string? value, string toolName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Fin.Fail<AgentRunId>(LanguageExt.Common.Error.New($"{toolName} requires a non-empty runId."))
            : Fin.Succ(new AgentRunId(value.Trim()));
    }

    public static string? NormalizeRouteTarget(string? routeTarget)
    {
        if (string.IsNullOrWhiteSpace(routeTarget))
        {
            return null;
        }

        var normalized = routeTarget.Trim().ToLowerInvariant();
        return normalized switch
        {
            "local" or "local-model" or "remote" or "remote-api" or "mcp" or "mcp-server" => normalized,
            _ => null
        };
    }

    private static Dictionary<string, string> BuildPublicMetadata(IReadOnlyDictionary<string, string?> metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (string.Equals(key, AgentRouterMetadataKeys.ApprovalToken, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result[key] = value.Trim();
        }

        return result;
    }

    private static string? ResolveMetadataValue(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static bool? ResolveBoolean(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "n", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }
}
