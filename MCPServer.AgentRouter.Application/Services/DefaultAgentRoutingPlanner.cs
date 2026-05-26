using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using Microsoft.Extensions.Logging;

namespace MCPServer.AgentRouter.Application.Services;

public sealed class DefaultAgentRoutingPlanner : IAgentRoutingPlanner
{
    private readonly IAgentObjectivePlanner _objectivePlanner;
    private readonly IAgentModelContextBuilder _contextBuilder;
    private readonly IAgentApprovalBoundary _approvalBoundary;
    private readonly ILogger<DefaultAgentRoutingPlanner> _logger;

    public DefaultAgentRoutingPlanner(
        IAgentObjectivePlanner objectivePlanner,
        IAgentModelContextBuilder contextBuilder,
        IAgentApprovalBoundary approvalBoundary,
        ILogger<DefaultAgentRoutingPlanner> logger)
    {
        _objectivePlanner = objectivePlanner ?? throw new ArgumentNullException(nameof(objectivePlanner));
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _approvalBoundary = approvalBoundary ?? throw new ArgumentNullException(nameof(approvalBoundary));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<Fin<AgentRoutingDecision>> PlanAsync(
        in AgentRouterRunRequest request,
        CancellationToken cancellationToken)
    {
        var capturedRequest = request;
        return PlanCoreAsync(capturedRequest, cancellationToken);
    }

    private async ValueTask<Fin<AgentRoutingDecision>> PlanCoreAsync(
        AgentRouterRunRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var contextResult = _contextBuilder.Build(in request);
        if (contextResult.IsFail)
        {
            return contextResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected context success while handling failure."),
                Fail: static error => Fin.Fail<AgentRoutingDecision>(error));
        }

        var context = contextResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var planResult = await _objectivePlanner.PlanAsync(context.Objective, cancellationToken).ConfigureAwait(false);
        if (planResult.IsFail)
        {
            return planResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected planning success while handling failure."),
                Fail: static error => Fin.Fail<AgentRoutingDecision>(error));
        }

        var plan = planResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var policyDecision = _approvalBoundary.Evaluate(context);
        var message = policyDecision.IsAllowed
            ? $"Prepared {context.Target.Name} route for objective '{context.Objective.Value}' with {plan.Steps.Count} planning step(s)."
            : policyDecision.Reason ?? $"Approval token required before {context.Target.Name} routing can proceed.";

        _logger.LogInformation(
            "AgentRouter planning decision prepared. Objective={Objective} Route={RouteName} Kind={RouteKind} ApprovalGranted={ApprovalGranted} Allowed={Allowed} RiskLevel={RiskLevel} StepCount={StepCount}.",
            context.Objective.Value,
            context.Target.Name,
            context.Target.Kind,
            context.ApprovalGranted,
            policyDecision.IsAllowed,
            policyDecision.RiskLevel,
            plan.Steps.Count);

        return Fin.Succ(new AgentRoutingDecision(
            Context: context,
            Plan: plan,
            Target: context.Target,
            PolicyDecision: policyDecision,
            Message: message));
    }
}
