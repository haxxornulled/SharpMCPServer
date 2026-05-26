using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Ssh.Interfaces;
using MCPServer.Ssh.Models;

namespace MCPServer.Tools.Ssh.Tools;

public sealed class SshAgentStatusTool : IMcpTool
{
    private static readonly JsonElement InputSchema = CreateInputSchema();
    private static readonly JsonElement OutputSchema = CreateOutputSchema();

    private readonly ISshAgentRuntime _agentRuntime;

    public SshAgentStatusTool(ISshAgentRuntime agentRuntime)
    {
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = SshToolNames.AgentStatus,
        Title = "Get SSH Agent Status",
        Description = "Returns current status, step state, and terminal output tails for a launched SSH agent.",
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

        var status = await _agentRuntime.GetStatusAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (status.IsFail)
        {
            return status.Match<Fin<ToolCallResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH agent status success while handling failure."),
                Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
        }

        var response = status.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH agent status failure while handling success."));

        var structuredContent = SshJson.ToAgentStatusJsonElement(response);
        return Fin.Succ<ToolCallResult>(ToolCallResult.Text(response.Summary, isError: response.Status == SshAgentStatusNames.Failed, structuredContent: structuredContent));
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
          "required": ["agentId", "status", "profile", "objective", "commandCount", "currentStep", "completedSteps", "failedSteps", "cancellationRequested", "currentCommand", "summary", "stdoutTail", "stderrTail", "stdoutLength", "stderrLength", "steps", "createdAt", "lastUpdatedAt", "completedAt"],
          "properties": {
            "agentId": { "type": "string" },
            "status": { "type": "string", "enum": ["queued", "working", "completed", "failed", "cancelled"] },
            "profile": { "type": "string" },
            "objective": { "type": "string" },
            "commandCount": { "type": "integer" },
            "currentStep": { "type": "integer" },
            "completedSteps": { "type": "integer" },
            "failedSteps": { "type": "integer" },
            "cancellationRequested": { "type": "boolean" },
            "currentCommand": { "type": "string" },
            "summary": { "type": "string" },
            "stdoutTail": { "type": "string" },
            "stderrTail": { "type": "string" },
            "stdoutLength": { "type": "integer" },
            "stderrLength": { "type": "integer" },
            "steps": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["index", "status", "command", "arguments", "exitCode", "summary", "startedAt", "completedAt"],
                "properties": {
                  "index": { "type": "integer" },
                  "status": { "type": "string" },
                  "command": { "type": "string" },
                  "arguments": { "type": "array", "items": { "type": "string" } },
                  "exitCode": { "type": ["integer", "null"] },
                  "summary": { "type": "string" },
                  "startedAt": { "type": ["string", "null"] },
                  "completedAt": { "type": ["string", "null"] }
                },
                "additionalProperties": false
              }
            },
            "createdAt": { "type": "string" },
            "lastUpdatedAt": { "type": "string" },
            "completedAt": { "type": ["string", "null"] }
          },
          "additionalProperties": false
        }
        """);
        return document.RootElement.Clone();
    }
}
