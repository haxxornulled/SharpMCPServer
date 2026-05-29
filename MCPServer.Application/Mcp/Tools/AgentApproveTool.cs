using System.Text.Json;
using LanguageExt;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Application.Models;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.Application.Mcp;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Tools;

public sealed class AgentApproveTool : IMcpTool
{
    private static readonly JsonElement InputSchema = AgentToolSchemas.CreateApproveInputSchema();
    private static readonly JsonElement OutputSchema = AgentToolSchemas.CreateOutputSchema();

    private readonly IAgentRunCoordinator _coordinator;

    public AgentApproveTool(IAgentRunCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = AgentToolNames.Approve,
        Title = "Approve Agent Run",
        Description = "Marks an awaiting agent run as approved and re-queues it.",
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Execution = new McpToolExecution
        {
            TaskSupport = McpToolTaskSupport.Forbidden
        }
    };

    public async ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestResult = AgentToolArguments.Parse(arguments, AgentRouterToolJsonSerializerContext.Default.AgentRunApproveRequest, AgentToolNames.Approve);
        if (requestResult.IsFail)
        {
            return Fail(requestResult);
        }

        var request = requestResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var runIdResult = AgentToolHelpers.ParseRunId(request.RunId, AgentToolNames.Approve);
        if (runIdResult.IsFail)
        {
            return Fail(runIdResult);
        }

        var runId = runIdResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var approvalRequest = new AgentRouterApproveRunRequest(
            runId,
            request.ApprovalId.Trim(),
            request.ApprovedBy,
            null);

        var approvedResult = await _coordinator.ApproveAsync(in approvalRequest, cancellationToken).ConfigureAwait(false);
        if (approvedResult.IsFail)
        {
            return Fail(approvedResult);
        }

        return BuildResult(approvedResult, $"Agent run '{runId.Value}' approved.");
    }

    private static Fin<ToolCallResult> BuildResult(Fin<AgentRunSnapshot> snapshotResult, string fallbackMessage)
    {
        var snapshot = snapshotResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));
        var structuredContent = AgentToolHelpers.BuildStructuredContent(snapshot);
        var json = JsonSerializer.SerializeToElement(structuredContent, AgentRouterToolJsonSerializerContext.Default.AgentRunStructuredContent);

        return Fin.Succ<ToolCallResult>(ToolCallResult.Text(
            snapshot.Message is { Length: > 0 } ? snapshot.Message : fallbackMessage,
            structuredContent: json));
    }

    private static Fin<ToolCallResult> Fail<T>(Fin<T> result)
    {
        var error = result.Match(
            Succ: static _ => throw new InvalidOperationException("Unexpected success while handling failure."),
            Fail: static value => value);
        return Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true));
    }
}
