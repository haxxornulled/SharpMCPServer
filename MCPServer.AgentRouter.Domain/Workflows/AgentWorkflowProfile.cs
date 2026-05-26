namespace MCPServer.AgentRouter.Domain.Workflows;

public sealed record AgentWorkflowProfile(
    AgentWorkflowMode Mode,
    bool RequiresApprovalBeforeExecution,
    bool AllowsDynamicPlanning,
    bool AllowsInspectAndContinue,
    string Description)
{
    public bool IsDeterministic => Mode.IsDeterministic;

    public bool IsAgentic => Mode.IsAgentic;

    public static AgentWorkflowProfile Deterministic()
    {
        return new AgentWorkflowProfile(
            AgentWorkflowMode.Deterministic,
            RequiresApprovalBeforeExecution: true,
            AllowsDynamicPlanning: false,
            AllowsInspectAndContinue: false,
            Description: "Validate, approve, execute, collect output, and complete using a predetermined path.");
    }

    public static AgentWorkflowProfile Agentic()
    {
        return new AgentWorkflowProfile(
            AgentWorkflowMode.Agentic,
            RequiresApprovalBeforeExecution: true,
            AllowsDynamicPlanning: true,
            AllowsInspectAndContinue: true,
            Description: "Plan, select capabilities, execute, inspect results, and continue or stop dynamically.");
    }

    public static bool TryCreate(AgentWorkflowMode mode, out AgentWorkflowProfile profile)
    {
        if (mode.IsDeterministic)
        {
            profile = Deterministic();
            return true;
        }

        if (mode.IsAgentic)
        {
            profile = Agentic();
            return true;
        }

        profile = default!;
        return false;
    }
}
