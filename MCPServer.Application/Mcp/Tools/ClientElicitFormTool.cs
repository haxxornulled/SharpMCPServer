using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Tools;

public sealed class ClientElicitFormTool : IMcpTool
{
    private static readonly JsonElement InputSchema = CreateInputSchema();
    private static readonly JsonElement OutputSchema = CreateOutputSchema();
    private readonly Func<IMcpClientFeatureInvoker> _clientFeaturesFactory;

    public ClientElicitFormTool(Func<IMcpClientFeatureInvoker> clientFeaturesFactory)
    {
        _clientFeaturesFactory = clientFeaturesFactory ?? throw new ArgumentNullException(nameof(clientFeaturesFactory));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = McpToolNames.ClientElicitForm,
        Title = "Client Elicitation Form",
        Description = "Requests client-side elicitation/create in form mode from the connected MCP client.",
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Execution = new McpToolExecution { TaskSupport = McpToolTaskSupport.Optional }
    };

    public async ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments is not { ValueKind: JsonValueKind.Object } supplied)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("client.elicit.form expects an arguments object.", isError: true));
        }

        if (!supplied.TryGetProperty("message"u8, out var messageElement) || messageElement.ValueKind != JsonValueKind.String)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("client.elicit.form requires a string message.", isError: true));
        }

        if (!supplied.TryGetProperty("requestedSchema"u8, out var schemaElement) || schemaElement.ValueKind != JsonValueKind.Object)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("client.elicit.form requires an object requestedSchema.", isError: true));
        }

        var task = supplied.TryGetProperty("task"u8, out var taskElement) && taskElement.ValueKind is JsonValueKind.True;
        var request = new ElicitRequestFormParams
        {
            Task = task ? new McpTaskMetadata() : null,
            Message = messageElement.GetString() ?? string.Empty,
            RequestedSchema = schemaElement.Clone()
        };

        var response = await _clientFeaturesFactory().ElicitFormAsync(request, cancellationToken).ConfigureAwait(false);
        return response.Match(
            Succ: payload => Fin.Succ<ToolCallResult>(ToolCallResult.Text(payload.GetRawText(), structuredContent: payload)),
            Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
    }

    private static JsonElement CreateInputSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["message", "requestedSchema"],
          "properties": {
            "message": { "type": "string", "minLength": 1 },
            "requestedSchema": { "type": "object" },
            "task": { "type": "boolean", "default": false }
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
          "properties": {
            "task": { "type": "object" },
            "action": { "type": "string" },
            "content": { "type": "object" }
          },
          "additionalProperties": true
        }
        """);
        return document.RootElement.Clone();
    }

}
