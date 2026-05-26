using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Policies;
using MCPServer.AgentRouter.Domain.Workflows;

namespace MCPServer.AgentRouter.PythonBridge.Native;

internal sealed class NativeBridgeRuntime
{
    public AgentRouterBridgeResponse Run(in AgentRouterBridgeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Objective))
        {
            throw new ArgumentException("agent objective is required.", nameof(request));
        }

        if (!AgentObjective.TryCreate(request.Objective, out var objective))
        {
            throw new ArgumentException("agent objective is required.", nameof(request));
        }

        var metadata = request.MetadataOrEmpty;
        var workflowProfile = ResolveWorkflowProfile(metadata);
        var target = ResolveTarget(metadata, workflowProfile);
        var approvalGranted = IsApprovalGranted(metadata);
        var policyDecision = EvaluateApproval(target, approvalGranted);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var status = policyDecision.IsAllowed
            ? AgentRouterRunStatuses.Completed
            : AgentRouterRunStatuses.Denied;

        var message = policyDecision.IsAllowed
            ? $"Prepared {target.Name} route for objective '{objective.Value}' with 4 planning step(s)."
            : policyDecision.Reason ?? $"Approval token required before target '{target.Name}' can touch external systems.";

        return new AgentRouterBridgeResponse(
            Status: status,
            Message: message,
            RunId: null,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: DateTimeOffset.UtcNow);
    }

    private static AgentWorkflowProfile ResolveWorkflowProfile(IReadOnlyDictionary<string, string?> metadata)
    {
        if (!TryGetValue(metadata, AgentRouterMetadataKeys.WorkflowMode, out var workflowModeValue) ||
            string.IsNullOrWhiteSpace(workflowModeValue))
        {
            return AgentWorkflowProfile.Deterministic();
        }

        if (!AgentWorkflowMode.TryCreate(workflowModeValue, out var workflowMode) ||
            !AgentWorkflowProfile.TryCreate(workflowMode, out var workflowProfile))
        {
            throw new ArgumentException($"Unknown workflow mode '{workflowModeValue}'.", nameof(metadata));
        }

        return workflowProfile;
    }

    private static NativeBridgeTarget ResolveTarget(
        IReadOnlyDictionary<string, string?> metadata,
        AgentWorkflowProfile workflowProfile)
    {
        if (!TryGetValue(metadata, AgentRouterMetadataKeys.RouteTarget, out var routeTargetValue) ||
            string.IsNullOrWhiteSpace(routeTargetValue))
        {
            return workflowProfile.IsAgentic
                ? NativeBridgeTarget.RemoteApi
                : NativeBridgeTarget.LocalModel;
        }

        return routeTargetValue.Trim().ToLowerInvariant() switch
        {
            "local" or "local-model" => NativeBridgeTarget.LocalModel,
            "remote" or "remote-api" => NativeBridgeTarget.RemoteApi,
            "mcp" or "mcp-server" => NativeBridgeTarget.McpServer,
            _ => throw new ArgumentException($"Unknown route target '{routeTargetValue}'.", nameof(metadata)),
        };
    }

    private static AgentPolicyDecision EvaluateApproval(NativeBridgeTarget target, bool approvalGranted)
    {
        if (!target.RequiresApproval)
        {
            return AgentPolicyDecision.Allowed(
                target.RiskLevel,
                $"Target '{target.Name}' stays within the host boundary.");
        }

        if (approvalGranted)
        {
            return AgentPolicyDecision.Allowed(
                target.RiskLevel,
                $"Approval token granted for target '{target.Name}'.");
        }

        return AgentPolicyDecision.AwaitingApproval(
            target.RiskLevel,
            $"Approval token required before target '{target.Name}' can touch external systems.");
    }

    private static bool IsApprovalGranted(IReadOnlyDictionary<string, string?> metadata)
    {
        if (TryGetBoolean(metadata, AgentRouterMetadataKeys.ApprovalGranted) is true)
        {
            return true;
        }

        return TryGetValue(metadata, AgentRouterMetadataKeys.ApprovalToken, out _);
    }

    private static bool TryGetValue(
        IReadOnlyDictionary<string, string?> metadata,
        string key,
        out string? value)
    {
        if (metadata.TryGetValue(key, out var rawValue) && !string.IsNullOrWhiteSpace(rawValue))
        {
            value = rawValue.Trim();
            return true;
        }

        value = null;
        return false;
    }

    private static bool? TryGetBoolean(
        IReadOnlyDictionary<string, string?> metadata,
        string key)
    {
        if (!metadata.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (bool.TryParse(rawValue, out var parsed))
        {
            return parsed;
        }

        var normalized = rawValue.Trim();
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

    private sealed record NativeBridgeTarget(string Name, string RiskLevel, bool RequiresApproval)
    {
        public static NativeBridgeTarget LocalModel { get; } = new(
            Name: "local-model",
            RiskLevel: AgentExecutionRiskLevels.Low,
            RequiresApproval: false);

        public static NativeBridgeTarget RemoteApi { get; } = new(
            Name: "remote-api",
            RiskLevel: AgentExecutionRiskLevels.Medium,
            RequiresApproval: true);

        public static NativeBridgeTarget McpServer { get; } = new(
            Name: "mcp-server",
            RiskLevel: AgentExecutionRiskLevels.High,
            RequiresApproval: true);
    }
}
