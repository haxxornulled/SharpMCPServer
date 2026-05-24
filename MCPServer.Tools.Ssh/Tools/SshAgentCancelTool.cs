using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Tools.Ssh.Interfaces;
using MCPServer.Tools.Ssh.Models;

namespace MCPServer.Tools.Ssh.Tools;

public sealed class SshAgentCancelTool : IMcpTool
{
    private static readonly JsonElement InputSchema = CreateInputSchema();
    private static readonly JsonElement OutputSchema = CreateOutputSchema();

    private readonly ISshAgentRuntime _agentRuntime;

    public SshAgentCancelTool(ISshAgentRuntime agentRuntime)
    {
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = SshToolNames.AgentCancel,
        Title = "Cancel SSH Agent",
        Description = "Requests cancellation of a running SSH agent.",
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Execution = new McpToolExecution { TaskSupport = McpToolTaskSupport.Forbidden }
    };

    public async ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var agentId = ReadRequiredString(arguments, "agentId");
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("agentId is required.", isError: true));
        }

        var cancelled = await _agentRuntime.CancelAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (cancelled.IsFail)
        {
            return cancelled.Match<Fin<ToolCallResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH agent cancel success while handling failure."),
                Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
        }

        var response = cancelled.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH agent cancel failure while handling success."));

        var structuredContent = SshJson.ToAgentCancelJsonElement(response);
        return Fin.Succ<ToolCallResult>(ToolCallResult.Text(response.Summary, structuredContent: structuredContent));
    }

    private static string ReadRequiredString(JsonElement? arguments, string name)
    {
        var root = arguments ?? default;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static JsonElement CreateInputSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["agentId"],
          "properties": { "agentId": { "type": "string", "minLength": 1 } },
          "additionalProperties": false
        }
        """);
        return document.RootElement.Clone();
    }

    private static JsonElement CreateOutputSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["agentId", "status", "cancellationRequested", "summary", "lastUpdatedAt"],
          "properties": {
            "agentId": { "type": "string" },
            "status": { "type": "string", "enum": ["queued", "working", "completed", "failed", "cancelled"] },
            "cancellationRequested": { "type": "boolean" },
            "summary": { "type": "string" },
            "lastUpdatedAt": { "type": "string" }
          },
          "additionalProperties": false
        }
        """);
        return document.RootElement.Clone();
    }
}
