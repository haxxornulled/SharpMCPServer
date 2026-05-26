using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Tools;

public sealed class ClientElicitUrlTool : IMcpTool
{
    private static readonly JsonElement InputSchema = CreateInputSchema();
    private static readonly JsonElement OutputSchema = CreateOutputSchema();
    private readonly IMcpClientFeatureInvoker _clientFeatures;

    public ClientElicitUrlTool(IMcpClientFeatureInvoker clientFeatures)
    {
        _clientFeatures = clientFeatures ?? throw new ArgumentNullException(nameof(clientFeatures));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = McpToolNames.ClientElicitUrl,
        Title = "Client Elicitation URL",
        Description = "Requests client-side elicitation/create in URL mode from the connected MCP client.",
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Execution = new McpToolExecution { TaskSupport = McpToolTaskSupport.Optional }
    };

    public async ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments is not { ValueKind: JsonValueKind.Object } supplied)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("client.elicit.url expects an arguments object.", isError: true));
        }

        if (!supplied.TryGetProperty("message"u8, out var messageElement) || messageElement.ValueKind != JsonValueKind.String)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("client.elicit.url requires a string message.", isError: true));
        }

        if (!supplied.TryGetProperty("elicitationId"u8, out var idElement) || idElement.ValueKind != JsonValueKind.String)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("client.elicit.url requires a string elicitationId.", isError: true));
        }

        if (!supplied.TryGetProperty("url"u8, out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("client.elicit.url requires a string url.", isError: true));
        }

        var task = supplied.TryGetProperty("task"u8, out var taskElement) && taskElement.ValueKind is JsonValueKind.True;
        var request = new ElicitRequestUrlParams
        {
            Task = task ? new McpTaskMetadata() : null,
            Message = messageElement.GetString() ?? string.Empty,
            ElicitationId = idElement.GetString() ?? string.Empty,
            Url = urlElement.GetString() ?? string.Empty
        };

        var response = await _clientFeatures.ElicitUrlAsync(request, cancellationToken).ConfigureAwait(false);
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
          "required": ["message", "elicitationId", "url"],
          "properties": {
            "message": { "type": "string", "minLength": 1 },
            "elicitationId": { "type": "string", "minLength": 1 },
            "url": { "type": "string", "minLength": 1 },
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
