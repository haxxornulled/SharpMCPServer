using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Abstractions;
using MCPServer.AgentRouter.Abstractions.Interfaces;
using MCPServer.AgentRouter.Abstractions.Models;
using MCPServer.AgentRouter.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MCPServer.AgentRouter.Application.Services;

public sealed class AgentRouterBridgeFacade : IAgentRouterBridgeFacade
{
    private readonly IAgentRoutingPlanner _routingPlanner;
    private readonly ILogger<AgentRouterBridgeFacade> _logger;

    public AgentRouterBridgeFacade(
        IAgentRoutingPlanner routingPlanner,
        ILogger<AgentRouterBridgeFacade> logger)
    {
        _routingPlanner = routingPlanner ?? throw new ArgumentNullException(nameof(routingPlanner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<Fin<AgentRouterBridgeResponse>> RunAsync(
        in AgentRouterBridgeRequest request,
        CancellationToken cancellationToken)
    {
        var capturedRequest = request;
        return RunCoreAsync(capturedRequest, cancellationToken);
    }

    private async ValueTask<Fin<AgentRouterBridgeResponse>> RunCoreAsync(
        AgentRouterBridgeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Objective))
        {
            return Fin.Fail<AgentRouterBridgeResponse>(Error.New("agent objective is required."));
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var routerRequest = new AgentRouterRunRequest(request.Objective, request.MetadataOrEmpty);
        var decisionResult = await _routingPlanner.PlanAsync(in routerRequest, cancellationToken).ConfigureAwait(false);
        if (decisionResult.IsFail)
        {
            return decisionResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected routing success while handling failure."),
                Fail: static error => Fin.Fail<AgentRouterBridgeResponse>(error));
        }

        var decision = decisionResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        _logger.LogInformation(
            "Python bridge planned route {RouteName} ({RouteKind}) for objective {Objective}. ApprovalGranted={ApprovalGranted} Allowed={Allowed} StepCount={StepCount}.",
            decision.Target.Name,
            decision.Target.Kind,
            decision.Context.Objective.Value,
            decision.Context.ApprovalGranted,
            decision.PolicyDecision.IsAllowed,
            decision.Plan.Steps.Count);

        var status = decision.PolicyDecision.IsAllowed
            ? AgentRouterRunStatuses.Completed
            : AgentRouterRunStatuses.Denied;

        return Fin.Succ(new AgentRouterBridgeResponse(
            Status: status,
            Message: decision.Message,
            RunId: null,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: DateTimeOffset.UtcNow));
    }
}
