using LanguageExt;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.Execution.Abstractions.Models;
using MCPServer.AgentRouter.Domain.Policies;
using MCPServer.Execution.Abstractions;

namespace MCPServer.AgentRouter.Application.Services;

public sealed class DefaultAgentPluginPolicyEvaluator : IAgentPluginPolicyEvaluator
{
    public ValueTask<Fin<AgentPolicyDecision>> EvaluateAsync(
        AgentPluginPolicyRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = request.ExecutionRequest.ParametersOrEmpty;
        var approvalGranted = IsApprovalGranted(metadata);
        var decision = AgentCapabilityApprovalPolicy.Evaluate(request.Capability, approvalGranted);

        return new ValueTask<Fin<AgentPolicyDecision>>(Fin.Succ(decision));
    }

    private static bool IsApprovalGranted(IReadOnlyDictionary<string, string?> metadata)
    {
        return TryGetBoolean(metadata, AgentRouterMetadataKeys.ApprovalGranted)
            ?? TryGetBoolean(metadata, "approval.granted")
            ?? TryGetBoolean(metadata, "approved")
            ?? false;
    }

    private static bool? TryGetBoolean(IReadOnlyDictionary<string, string?> metadata, string key)
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
        if (string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "n", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }
}
