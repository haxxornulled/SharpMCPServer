using System.Text.Json;
using LanguageExt;
using MCPServer.AgentRouter.Application.Interfaces;
using MCPServer.AgentRouter.Domain.Runs;
using MCPServer.Application.Mcp;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Tools;

public sealed class AgentSubagentCreateTool : IMcpTool
{
    private static readonly JsonElement InputSchema = AgentToolSchemas.CreateSubagentCreateInputSchema();
    private static readonly JsonElement OutputSchema = AgentToolSchemas.CreateOutputSchema();

    private readonly IAgentRunCoordinator _coordinator;

    public AgentSubagentCreateTool(IAgentRunCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = AgentToolNames.SubagentCreate,
        Title = "Create Subagent Run",
        Description = "Creates a subagent run linked to an existing parent run.",
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Execution = new McpToolExecution
        {
            TaskSupport = McpToolTaskSupport.Optional
        }
    };

    public async ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestResult = AgentToolArguments.Parse(arguments, AgentRouterToolJsonSerializerContext.Default.AgentRunCreateRequest, AgentToolNames.SubagentCreate);
        if (requestResult.IsFail)
        {
            return Fail(requestResult);
        }

        var request = requestResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var startRequestResult = AgentToolHelpers.CreateStartRequest(request, isSubagent: true, AgentToolNames.SubagentCreate);
        if (startRequestResult.IsFail)
        {
            return Fail(startRequestResult);
        }

        var startRequest = startRequestResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException(error.Message));

        var started = await _coordinator.StartAsync(in startRequest, cancellationToken).ConfigureAwait(false);
        if (started.IsFail)
        {
            return Fail(started);
        }

        var runId = started.Match(
            Succ: static value => value.RunId,
            Fail: static error => throw new InvalidOperationException(error.Message));

        return await RenderSnapshotAsync(runId, cancellationToken, $"Created subagent run '{runId.Value}'.").ConfigureAwait(false);
    }

    private async ValueTask<Fin<ToolCallResult>> RenderSnapshotAsync(AgentRunId runId, CancellationToken cancellationToken, string fallbackMessage)
    {
        var snapshotResult = await AgentToolHelpers.GetSnapshotAsync(_coordinator, runId, AgentToolNames.SubagentCreate, cancellationToken).ConfigureAwait(false);
        if (snapshotResult.IsFail)
        {
            return Fail(snapshotResult);
        }

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
