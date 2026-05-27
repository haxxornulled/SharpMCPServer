using System.Buffers;
using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;

namespace MCPServer.Tools.Inference.Tools;

public sealed class InferenceGenerateTool : IMcpTool
{
    private static readonly JsonElement InputSchema = InferenceToolSchemas.CreateGenerateInputSchema();
    private static readonly JsonElement OutputSchema = InferenceToolSchemas.CreateGenerateOutputSchema();

    private readonly IInferenceRouter _inferenceRouter;

    public InferenceGenerateTool(IInferenceRouter inferenceRouter)
    {
        _inferenceRouter = inferenceRouter ?? throw new ArgumentNullException(nameof(inferenceRouter));
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = InferenceToolNames.Generate,
        Title = "Generate Inference Response",
        Description = "Routes a text prompt through the configured inference providers and returns the selected provider response.",
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

        if (arguments is not { ValueKind: JsonValueKind.Object } supplied)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("inference.generate expects an arguments object.", isError: true));
        }

        if (!TryReadString(supplied, "prompt"u8, out var prompt) || string.IsNullOrWhiteSpace(prompt))
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("inference.generate requires a string prompt.", isError: true));
        }

        var systemPrompt = TryReadOptionalString(supplied, "systemPrompt"u8);
        var model = TryReadOptionalString(supplied, "model"u8);
        var maxTokens = TryReadOptionalInt32(supplied, "maxTokens"u8);
        var temperature = TryReadOptionalDouble(supplied, "temperature"u8);
        var providerId = NormalizeOptionalString(TryReadOptionalString(supplied, "providerId"u8));
        InferenceRoutingStrategy? strategy;
        try
        {
            strategy = TryReadOptionalStrategy(supplied, "strategy"u8);
        }
        catch (ArgumentException ex)
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text(ex.Message, isError: true));
        }
        var fallbackProviderIds = TryReadStringArray(supplied, "fallbackProviderIds"u8);

        InferenceRoutingHint? routingHint = null;
        if (providerId is not null || strategy is not null || fallbackProviderIds.Count > 0)
        {
            routingHint = new InferenceRoutingHint(
                strategy ?? InferenceRoutingStrategy.PrimaryThenFallback,
                providerId,
                fallbackProviderIds.Count > 0 ? fallbackProviderIds : null);
        }

        var messages = new List<InferenceMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new InferenceMessage(InferenceRole.System, systemPrompt));
        }

        messages.Add(new InferenceMessage(InferenceRole.User, prompt));

        var request = new InferenceRequest(
            messages,
            model,
            maxTokens,
            temperature,
            routingHint);

        var result = await _inferenceRouter.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Match<Fin<ToolCallResult>>(
            Succ: response =>
            {
                var structuredContent = CreateStructuredContent(response);
                return Fin.Succ<ToolCallResult>(ToolCallResult.Text(
                    response.Content,
                    structuredContent: structuredContent));
            },
            Fail: error => Fin.Succ<ToolCallResult>(ToolCallResult.Text(error.Message, isError: true)));
    }

    private static JsonElement CreateStructuredContent(InferenceResponse response)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("providerId", response.ProviderId);
            writer.WriteString("model", response.Model);
            writer.WriteString("content", response.Content);

            if (!string.IsNullOrWhiteSpace(response.FinishReason))
            {
                writer.WriteString("finishReason", response.FinishReason);
            }

            if (response.Usage is { } usage)
            {
                writer.WritePropertyName("usage");
                writer.WriteStartObject();
                if (usage.InputTokens is int inputTokens)
                {
                    writer.WriteNumber("inputTokens", inputTokens);
                }
                else
                {
                    writer.WriteNull("inputTokens");
                }

                if (usage.OutputTokens is int outputTokens)
                {
                    writer.WriteNumber("outputTokens", outputTokens);
                }
                else
                {
                    writer.WriteNull("outputTokens");
                }

                if (usage.TotalTokens is int totalTokens)
                {
                    writer.WriteNumber("totalTokens", totalTokens);
                }
                else
                {
                    writer.WriteNull("totalTokens");
                }

                writer.WriteEndObject();
            }

            if (response.Metadata is { Count: > 0 } metadata)
            {
                writer.WritePropertyName("metadata");
                writer.WriteStartObject();
                foreach (var pair in metadata)
                {
                    writer.WriteString(pair.Key, pair.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory.ToArray());
        return document.RootElement.Clone();
    }

    private static bool TryReadString(JsonElement element, ReadOnlySpan<byte> propertyName, out string? value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static string? TryReadOptionalString(JsonElement element, ReadOnlySpan<byte> propertyName)
    {
        return TryReadString(element, propertyName, out var value) ? value : null;
    }

    private static int? TryReadOptionalInt32(JsonElement element, ReadOnlySpan<byte> propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsedValue)
            ? parsedValue
            : null;
    }

    private static double? TryReadOptionalDouble(JsonElement element, ReadOnlySpan<byte> propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var doubleValue))
        {
            return doubleValue;
        }

        return property.ValueKind == JsonValueKind.String && double.TryParse(property.GetString(), out var parsedValue)
            ? parsedValue
            : null;
    }

    private static List<string> TryReadStringArray(JsonElement element, ReadOnlySpan<byte> propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return values;
    }

    private static InferenceRoutingStrategy? TryReadOptionalStrategy(JsonElement element, ReadOnlySpan<byte> propertyName)
    {
        var strategyRaw = TryReadOptionalString(element, propertyName);
        if (string.IsNullOrWhiteSpace(strategyRaw))
        {
            return null;
        }

        return Enum.TryParse<InferenceRoutingStrategy>(strategyRaw.Trim(), ignoreCase: true, out var strategy)
            ? strategy
            : throw new ArgumentException($"Unsupported inference routing strategy '{strategyRaw}'.");
    }

    private static string? NormalizeOptionalString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
