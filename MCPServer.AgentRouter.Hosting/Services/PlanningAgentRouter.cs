using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using Microsoft.Extensions.Logging;

namespace MCPServer.AgentRouter.Hosting.Services;

public sealed class PlanningAgentRouter : IAgentRouter
{
    private readonly IAgentRoutingPlanner _routingPlanner;
    private readonly ILogger<PlanningAgentRouter> _logger;

    public PlanningAgentRouter(
        IAgentRoutingPlanner routingPlanner,
        ILogger<PlanningAgentRouter> logger)
    {
        _routingPlanner = routingPlanner ?? throw new ArgumentNullException(nameof(routingPlanner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<Fin<AgentRouterRunResult>> RunAsync(
        in AgentRouterRunRequest request,
        CancellationToken cancellationToken)
    {
        var capturedRequest = request;
        return RunCoreAsync(capturedRequest, cancellationToken);
    }

    private async ValueTask<Fin<AgentRouterRunResult>> RunCoreAsync(
        AgentRouterRunRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Objective))
        {
            return Fin.Fail<AgentRouterRunResult>(Error.New("agent objective is required."));
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var decisionResult = await _routingPlanner.PlanAsync(in request, cancellationToken).ConfigureAwait(false);
        if (decisionResult.IsFail)
        {
            return decisionResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected routing success while handling failure."),
                Fail: static error => Fin.Fail<AgentRouterRunResult>(error));
        }

        var decision = decisionResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        _logger.LogInformation(
            "Planning router prepared route {RouteName} ({RouteKind}) for objective {Objective}. ApprovalGranted={ApprovalGranted} Allowed={Allowed} StepCount={StepCount}.",
            decision.Target.Name,
            decision.Target.Kind,
            decision.Context.Objective.Value,
            decision.Context.ApprovalGranted,
            decision.PolicyDecision.IsAllowed,
            decision.Plan.Steps.Count);

        var status = decision.PolicyDecision.IsAllowed
            ? AgentRouterRunStatuses.Completed
            : AgentRouterRunStatuses.Denied;

        var result = new AgentRouterRunResult(
            Status: status,
            Message: decision.Message,
            RunId: null,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: DateTimeOffset.UtcNow);

        return Fin.Succ(result);
    }
}
