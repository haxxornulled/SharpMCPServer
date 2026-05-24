using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Tools.Ssh.Interfaces;
using MCPServer.Tools.Ssh.Models;

namespace MCPServer.Tools.Ssh.Tools;

public sealed class SshAgentOutputTool : IMcpTool
{
    private static readonly JsonElement InputSchema = CreateInputSchema();
    private static readonly JsonElement OutputSchema = CreateOutputSchema();

    private readonly ISshAgentRuntime _agentRuntime;

    public SshAgentOutputTool(ISshAgentRuntime agentRuntime)
    {
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = SshToolNames.AgentOutput,
        Title = "Read SSH Agent Output",
        Description = "Reads incremental stdout/stderr text from a launched SSH agent using offsets.",
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Execution = new McpToolExecution { TaskSupport = McpToolTaskSupport.Forbidden }
    };

    public async ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = SshAgentOutputRequest.FromArguments(arguments);
        var output = await _agentRuntime.GetOutputAsync(request, cancellationToken).ConfigureAwait(false);

        if (output.IsFail)
        {
            return output.Match<Fin<ToolCallResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH agent output success while handling failure."),
                Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
        }

        var response = output.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH agent output failure while handling success."));

        var structuredContent = SshJson.ToAgentOutputJsonElement(response);
        var text = string.IsNullOrEmpty(response.Stdout) && string.IsNullOrEmpty(response.Stderr)
            ? "No new SSH agent output."
            : response.Stdout + response.Stderr;

        return Fin.Succ<ToolCallResult>(ToolCallResult.Text(text, structuredContent: structuredContent));
    }

    private static JsonElement CreateInputSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["agentId"],
          "properties": {
            "agentId": { "type": "string", "minLength": 1 },
            "stdoutOffset": { "type": "integer", "minimum": 0, "default": 0 },
            "stderrOffset": { "type": "integer", "minimum": 0, "default": 0 },
            "maxChars": { "type": "integer", "minimum": 1, "maximum": 100000, "default": 20000 }
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
          "required": ["agentId", "status", "stdout", "stderr", "stdoutOffset", "stderrOffset", "nextStdoutOffset", "nextStderrOffset", "stdoutTruncated", "stderrTruncated"],
          "properties": {
            "agentId": { "type": "string" },
            "status": { "type": "string", "enum": ["queued", "working", "completed", "failed", "cancelled"] },
            "stdout": { "type": "string" },
            "stderr": { "type": "string" },
            "stdoutOffset": { "type": "integer" },
            "stderrOffset": { "type": "integer" },
            "nextStdoutOffset": { "type": "integer" },
            "nextStderrOffset": { "type": "integer" },
            "stdoutTruncated": { "type": "boolean" },
            "stderrTruncated": { "type": "boolean" }
          },
          "additionalProperties": false
        }
        """);
        return document.RootElement.Clone();
    }
}
