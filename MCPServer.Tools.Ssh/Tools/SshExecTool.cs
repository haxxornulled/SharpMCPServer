using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Tools.Ssh.Interfaces;
using MCPServer.Tools.Ssh.Models;

namespace MCPServer.Tools.Ssh.Tools;

public sealed class SshExecTool : IMcpTool
{
    private static readonly JsonElement InputSchema = CreateInputSchema();
    private static readonly JsonElement OutputSchema = CreateOutputSchema();

    private readonly ISshExecutionService _executionService;

    public SshExecTool(ISshExecutionService executionService)
    {
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = SshToolNames.Exec,
        Title = "SSH Execute Command",
        Description = "Executes an allowlisted command through a configured SSH profile. Credentials are resolved from profile configuration only; tool callers cannot supply secrets.",
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
        var request = SshExecutionRequest.FromArguments(arguments);
        var execution = await _executionService.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

        if (execution.IsFail)
        {
            return execution.Match<Fin<ToolCallResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH execution success while handling failure."),
                Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
        }

        var response = execution.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH execution failure while handling success."));

        var structuredContent = SshJson.ToJsonElement(response);
        var text = response.Allowed
            ? response.Summary
            : response.PolicyReason ?? response.Summary;

        return Fin.Succ<ToolCallResult>(ToolCallResult.Text(
            text,
            isError: !string.Equals(response.Status, SshExecutionStatusNames.Succeeded, StringComparison.Ordinal),
            structuredContent: structuredContent));
    }

    private static JsonElement CreateInputSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["profile", "command"],
          "properties": {
            "profile": { "type": "string", "minLength": 1, "description": "Configured SSH profile name." },
            "command": { "type": "string", "minLength": 1, "description": "Executable name only; paths and inline shell text are rejected by policy." },
            "arguments": {
              "type": "array",
              "items": { "type": "string" },
              "default": []
            },
            "workingDirectory": { "type": "string", "minLength": 1 },
            "timeoutSeconds": { "type": "integer", "minimum": 1, "maximum": 600 },
            "operationKey": { "type": "string", "minLength": 1, "description": "Optional correlation key exported as MCP_OPERATION_KEY on the remote command." }
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
          "required": ["id", "status", "allowed", "policyDecision", "profile", "command", "arguments", "workingDirectory", "exitCode", "timedOut", "stdout", "stderr", "stdoutTruncated", "stderrTruncated", "elapsedMilliseconds", "summary", "traceId", "createdAt", "completedAt"],
          "properties": {
            "id": { "type": "string", "minLength": 1 },
            "status": { "type": "string", "enum": ["denied", "failed", "succeeded", "timed_out"] },
            "allowed": { "type": "boolean" },
            "policyDecision": { "type": "string", "minLength": 1 },
            "policyReason": { "type": "string" },
            "profile": { "type": "string" },
            "command": { "type": "string" },
            "arguments": { "type": "array", "items": { "type": "string" } },
            "workingDirectory": { "type": "string" },
            "exitCode": { "type": ["integer", "null"] },
            "timedOut": { "type": "boolean" },
            "stdout": { "type": "string" },
            "stderr": { "type": "string" },
            "stdoutTruncated": { "type": "boolean" },
            "stderrTruncated": { "type": "boolean" },
            "elapsedMilliseconds": { "type": "integer", "minimum": 0 },
            "summary": { "type": "string" },
            "traceId": { "type": "string" },
            "createdAt": { "type": "string" },
            "completedAt": { "type": "string" }
          },
          "additionalProperties": false
        }
        """);

        return document.RootElement.Clone();
    }
}
