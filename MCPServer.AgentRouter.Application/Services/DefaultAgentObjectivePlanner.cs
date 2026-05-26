using LanguageExt;
using LanguageExt.Common;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Domain.Objectives;
using MCPServer.AgentRouter.Domain.Plans;

namespace MCPServer.AgentRouter.Application.Services;

public sealed class DefaultAgentObjectivePlanner : IAgentObjectivePlanner
{
    public ValueTask<Fin<AgentPlan>> PlanAsync(
        AgentObjective objective,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (objective.IsEmpty)
        {
            return new ValueTask<Fin<AgentPlan>>(Fin.Fail<AgentPlan>(Error.New("agent objective is required.")));
        }

        var steps = new[]
        {
            new AgentPlanStep(
                Order: 1,
                Name: "Normalize objective",
                CapabilityName: "planning.normalize",
                Description: "Trim, validate, and normalize the objective before any routing decision."),
            new AgentPlanStep(
                Order: 2,
                Name: "Build model context",
                CapabilityName: "planning.context",
                Description: "Prepare a prompt-safe context with control-plane data stripped out."),
            new AgentPlanStep(
                Order: 3,
                Name: "Apply approval boundary",
                CapabilityName: "planning.approval",
                Description: "Require a token before any route that can touch external systems."),
            new AgentPlanStep(
                Order: 4,
                Name: "Select execution route",
                CapabilityName: "planning.routing",
                Description: "Choose the local model, remote API, or MCP server route."),
        };

        var plan = new AgentPlan(
            Objective: objective,
            Steps: steps,
            Summary: $"Host-side plan prepared for '{objective.Value}'.");

        return new ValueTask<Fin<AgentPlan>>(Fin.Succ(plan));
    }
}
