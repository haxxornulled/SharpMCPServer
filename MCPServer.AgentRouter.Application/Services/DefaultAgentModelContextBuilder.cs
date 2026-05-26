using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Workflows;

namespace MCPServer.AgentRouter.Application.Services;

public sealed class DefaultAgentModelContextBuilder : IAgentModelContextBuilder
{
    public Fin<AgentModelContext> Build(in AgentRouterRunRequest request)
    {
        if (!AgentObjective.TryCreate(request.Objective, out var objective))
        {
            return Fin.Fail<AgentModelContext>(Error.New("agent objective is required."));
        }

        var metadata = request.MetadataOrEmpty;
        var workflowProfileResult = BuildWorkflowProfile(metadata);
        if (workflowProfileResult.IsFail)
        {
            return workflowProfileResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected workflow profile success while handling failure."),
                Fail: static error => Fin.Fail<AgentModelContext>(error));
        }

        var workflowProfile = workflowProfileResult.Match(
            Succ: static profile => profile,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var targetResult = BuildTarget(metadata, workflowProfile);
        if (targetResult.IsFail)
        {
            return targetResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected routing target success while handling failure."),
                Fail: static error => Fin.Fail<AgentModelContext>(error));
        }

        var target = targetResult.Match(
            Succ: static target => target,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var promptMetadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in metadata)
        {
            if (!item.Key.StartsWith("agent.", StringComparison.OrdinalIgnoreCase))
            {
                promptMetadata[item.Key] = item.Value;
            }
        }

        var approvalGranted = IsApprovalGranted(metadata);

        return Fin.Succ(new AgentModelContext(
            Objective: objective,
            WorkflowProfile: workflowProfile,
            Target: target,
            PromptMetadata: promptMetadata,
            ApprovalGranted: approvalGranted));
    }

    private static Fin<AgentWorkflowProfile> BuildWorkflowProfile(IReadOnlyDictionary<string, string?> metadata)
    {
        if (!TryGetValue(metadata, AgentRouterMetadataKeys.WorkflowMode, out var workflowModeValue) ||
            string.IsNullOrWhiteSpace(workflowModeValue))
        {
            return Fin.Succ(AgentWorkflowProfile.Deterministic());
        }

        if (!AgentWorkflowMode.TryCreate(workflowModeValue, out var workflowMode) ||
            !AgentWorkflowProfile.TryCreate(workflowMode, out var workflowProfile))
        {
            return Fin.Fail<AgentWorkflowProfile>(Error.New($"Unknown workflow mode '{workflowModeValue}'."));
        }

        return Fin.Succ(workflowProfile);
    }

    private static Fin<AgentRoutingTarget> BuildTarget(
        IReadOnlyDictionary<string, string?> metadata,
        AgentWorkflowProfile workflowProfile)
    {
        if (!TryGetValue(metadata, AgentRouterMetadataKeys.RouteTarget, out var routeTargetValue) ||
            string.IsNullOrWhiteSpace(routeTargetValue))
        {
            return Fin.Succ(workflowProfile.IsAgentic
                ? AgentRoutingTarget.RemoteApi
                : AgentRoutingTarget.LocalModel);
        }

        return routeTargetValue.Trim().ToLowerInvariant() switch
        {
            "local" or "local-model" => Fin.Succ(AgentRoutingTarget.LocalModel),
            "remote" or "remote-api" => Fin.Succ(AgentRoutingTarget.RemoteApi),
            "mcp" or "mcp-server" => Fin.Succ(AgentRoutingTarget.McpServer),
            _ => Fin.Fail<AgentRoutingTarget>(Error.New($"Unknown route target '{routeTargetValue}'.")),
        };
    }

    private static bool IsApprovalGranted(IReadOnlyDictionary<string, string?> metadata)
    {
        if (TryGetBoolean(metadata, AgentRouterMetadataKeys.ApprovalGranted) is true)
        {
            return true;
        }

        if (TryGetValue(metadata, AgentRouterMetadataKeys.ApprovalToken, out _))
        {
            return true;
        }

        return false;
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
}
