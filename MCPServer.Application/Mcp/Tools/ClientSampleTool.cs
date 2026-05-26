using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Tools;

public sealed class ClientSampleTool : IMcpTool
{
    private static readonly JsonElement InputSchema = CreateInputSchema();
    private static readonly JsonElement OutputSchema = CreateOutputSchema();
    private readonly IMcpClientFeatureInvoker _clientFeatures;

    public ClientSampleTool(IMcpClientFeatureInvoker clientFeatures)
    {
        _clientFeatures = clientFeatures ?? throw new ArgumentNullException(nameof(clientFeatures));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = McpToolNames.ClientSample,
        Title = "Client Sampling Request",
        Description = "Requests client-side sampling/createMessage from the connected MCP client.",
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Execution = new McpToolExecution { TaskSupport = McpToolTaskSupport.Optional }
    };

    public async ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments is not { ValueKind: JsonValueKind.Object } supplied)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("client.sample expects an arguments object.", isError: true));
        }

        if (!supplied.TryGetProperty("prompt"u8, out var promptElement) || promptElement.ValueKind != JsonValueKind.String)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("client.sample requires a string prompt.", isError: true));
        }

        var prompt = promptElement.GetString() ?? string.Empty;
        var maxTokens = supplied.TryGetProperty("maxTokens"u8, out var maxTokensElement) && maxTokensElement.TryGetInt32(out var parsedMaxTokens)
            ? parsedMaxTokens
            : 512;
        var task = supplied.TryGetProperty("task"u8, out var taskElement) && taskElement.ValueKind is JsonValueKind.True;
        var systemPrompt = supplied.TryGetProperty("systemPrompt"u8, out var systemPromptElement) && systemPromptElement.ValueKind == JsonValueKind.String
            ? systemPromptElement.GetString()
            : null;
        var temperature = supplied.TryGetProperty("temperature"u8, out var temperatureElement) && temperatureElement.TryGetDouble(out var parsedTemperature)
            ? parsedTemperature
            : (double?)null;

        var request = new CreateMessageRequestParams
        {
            Task = task ? new McpTaskMetadata() : null,
            Messages =
            [
                new SamplingMessage
                {
                    Role = "user",
                    Content = JsonSerializer.SerializeToElement(prompt, McpJsonSerializerContext.Default.String)
                }
            ],
            SystemPrompt = systemPrompt,
            Temperature = temperature,
            MaxTokens = maxTokens
        };

        var response = await _clientFeatures.CreateMessageAsync(request, cancellationToken).ConfigureAwait(false);
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
          "required": ["prompt"],
          "properties": {
            "prompt": { "type": "string", "minLength": 1 },
            "systemPrompt": { "type": "string" },
            "maxTokens": { "type": "integer", "minimum": 1 },
            "temperature": { "type": "number" },
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
            "model": { "type": "string" },
            "stopReason": { "type": "string" },
            "role": { "type": "string" },
            "content": {}
          },
          "additionalProperties": true
        }
        """);
        return document.RootElement.Clone();
    }

}
