using System.Text.Json;
using LanguageExt;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.Application.Mcp;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Tools;

public sealed class AgentStatusTool : IMcpTool
{
    private static readonly JsonElement InputSchema = AgentToolSchemas.CreateTargetInputSchema();
    private static readonly JsonElement OutputSchema = AgentToolSchemas.CreateOutputSchema();

    private readonly IAgentRunCoordinator _coordinator;

    public AgentStatusTool(IAgentRunCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = AgentToolNames.Status,
        Title = "Agent Run Status",
        Description = "Returns the current snapshot for an agent run.",
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

        var requestResult = AgentToolArguments.Parse(arguments, AgentRouterToolJsonSerializerContext.Default.AgentRunTargetRequest, AgentToolNames.Status);
        if (requestResult.IsFail)
        {
            return Fail(requestResult);
        }

        var request = requestResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var runIdResult = AgentToolHelpers.ParseRunId(request.RunId, AgentToolNames.Status);
        if (runIdResult.IsFail)
        {
            return Fail(runIdResult);
        }

        var runId = runIdResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var snapshotResult = await _coordinator.GetSnapshotAsync(runId, cancellationToken).ConfigureAwait(false);
        if (snapshotResult.IsFail)
        {
            return Fail(snapshotResult);
        }

        return BuildResult(snapshotResult, $"Agent run '{runId.Value}' is currently {snapshotResult.Match(Succ: static value => value.Status, Fail: static _ => string.Empty)}.");
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
