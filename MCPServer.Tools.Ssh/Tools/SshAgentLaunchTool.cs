using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Tools.Ssh.Interfaces;
using MCPServer.Tools.Ssh.Models;

namespace MCPServer.Tools.Ssh.Tools;

public sealed class SshAgentLaunchTool : IMcpTool
{
    private static readonly JsonElement InputSchema = CreateInputSchema();
    private static readonly JsonElement OutputSchema = CreateOutputSchema();

    private readonly ISshAgentRuntime _agentRuntime;

    public SshAgentLaunchTool(ISshAgentRuntime agentRuntime)
    {
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = SshToolNames.AgentLaunch,
        Title = "Launch SSH Agent",
        Description = "Launches a background SSH agent run that executes a policy-checked command sequence through a configured SSH profile. Use ssh.agent.status and ssh.agent.output to monitor terminal output.",
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
        var request = SshAgentLaunchRequest.FromArguments(arguments);
        var launched = await _agentRuntime.LaunchAsync(request, cancellationToken).ConfigureAwait(false);

        if (launched.IsFail)
        {
            return launched.Match<Fin<ToolCallResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH agent launch success while handling failure."),
                Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
        }

        var response = launched.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH agent launch failure while handling success."));

        var structuredContent = SshJson.ToAgentLaunchJsonElement(response);
        return Fin.Succ<ToolCallResult>(ToolCallResult.Text(response.Summary, structuredContent: structuredContent));
    }

    private static JsonElement CreateInputSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["profile", "objective", "commands"],
          "properties": {
            "profile": { "type": "string", "minLength": 1 },
            "objective": { "type": "string", "minLength": 1, "description": "Human readable task objective, e.g. configure nginx on Debian 13." },
            "workingDirectory": { "type": "string", "minLength": 1 },
            "timeoutSecondsPerStep": { "type": "integer", "minimum": 1, "maximum": 600 },
            "operationKey": { "type": "string", "minLength": 1 },
            "commands": {
              "type": "array",
              "minItems": 1,
              "maxItems": 100,
              "items": {
                "type": "object",
                "required": ["command"],
                "properties": {
                  "command": { "type": "string", "minLength": 1, "description": "Executable name only. Existing SSH policy still decides whether this is allowed." },
                  "arguments": { "type": "array", "items": { "type": "string" }, "default": [] },
                  "workingDirectory": { "type": "string", "minLength": 1 },
                  "timeoutSeconds": { "type": "integer", "minimum": 1, "maximum": 600 }
                },
                "additionalProperties": false
              }
            }
          },
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
          "required": ["agentId", "status", "profile", "objective", "commandCount", "currentStep", "pollIntervalMilliseconds", "createdAt", "lastUpdatedAt", "summary"],
          "properties": {
            "agentId": { "type": "string" },
            "status": { "type": "string", "enum": ["queued", "working", "completed", "failed", "cancelled"] },
            "profile": { "type": "string" },
            "objective": { "type": "string" },
            "commandCount": { "type": "integer" },
            "currentStep": { "type": "integer" },
            "pollIntervalMilliseconds": { "type": "integer" },
            "createdAt": { "type": "string" },
            "lastUpdatedAt": { "type": "string" },
            "summary": { "type": "string" }
          },
          "additionalProperties": false
        }
        """);

        return document.RootElement.Clone();
    }
}
