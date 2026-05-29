using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace MCPServer.Inference.Infrastructure.Providers;

public sealed class OllamaInferenceClient : OpenAiCompatibleInferenceClientBase
{
    public const string ProviderName = "ollama";

    public OllamaInferenceClient(
        IHttpClientFactory httpClientFactory,
        McpInferenceOptions options,
        ILogger<OllamaInferenceClient> logger)
        : base(httpClientFactory, options, logger)
    {
    }

    public override string ProviderId => ProviderName;

    public override string DisplayName => "Ollama";

    protected override string GetRequestPath() => "api/chat";

    protected override string GetProbePath() => "api/tags";

    protected override string BuildRequestJson(InferenceRequest request, McpInferenceProviderOptions providerOptions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            var model = string.IsNullOrWhiteSpace(request.Model)
                ? providerOptions.Model
                : request.Model.Trim();
            var effectiveMaxTokens = request.MaxTokens is int requestMaxTokens && requestMaxTokens > 0
                ? requestMaxTokens
                : providerOptions.MaxTokens;
            var effectiveTemperature = request.Temperature ?? providerOptions.Temperature;
            var hasOptions =
                providerOptions.ContextLength is int contextLength && contextLength > 0 ||
                effectiveMaxTokens is int maxTokens && maxTokens > 0 ||
                effectiveTemperature is double ||
                providerOptions.TopP is double ||
                providerOptions.TopK is int topK && topK > 0 ||
                providerOptions.RepeatPenalty is double ||
                providerOptions.Seed is int;

            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WritePropertyName("messages");
            writer.WriteStartArray();

            foreach (var message in request.Messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", ToProviderRole(message.Role));
                writer.WriteString("content", message.Content);

                if (!string.IsNullOrWhiteSpace(message.Name))
                {
                    if (message.Role == InferenceRole.Tool)
                    {
                        writer.WriteString("tool_name", message.Name);
                    }
                    else
                    {
                        writer.WriteString("name", message.Name);
                    }
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (!string.IsNullOrWhiteSpace(providerOptions.KeepAlive))
            {
                writer.WriteString("keep_alive", providerOptions.KeepAlive.Trim());
            }

            if (hasOptions)
            {
                writer.WritePropertyName("options");
                writer.WriteStartObject();

                if (providerOptions.ContextLength is int contextLengthValue && contextLengthValue > 0)
                {
                    writer.WriteNumber("num_ctx", contextLengthValue);
                }

                if (effectiveMaxTokens is int maxTokensValue && maxTokensValue > 0)
                {
                    writer.WriteNumber("num_predict", maxTokensValue);
                }

                if (effectiveTemperature is double temperature)
                {
                    writer.WriteNumber("temperature", temperature);
                }

                if (providerOptions.TopP is double topP)
                {
                    writer.WriteNumber("top_p", topP);
                }

                if (providerOptions.TopK is int topKValue && topKValue > 0)
                {
                    writer.WriteNumber("top_k", topKValue);
                }

                if (providerOptions.RepeatPenalty is double repeatPenalty)
                {
                    writer.WriteNumber("repeat_penalty", repeatPenalty);
                }

                if (providerOptions.Seed is int seed)
                {
                    writer.WriteNumber("seed", seed);
                }

                writer.WriteEndObject();
            }

            writer.WriteBoolean("stream", false);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    protected override string ExtractContent(JsonElement root)
    {
        if (root.TryGetProperty("message", out var messageElement))
        {
            var content = ReadContentElement(messageElement);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        if (root.TryGetProperty("response", out var responseElement) &&
            responseElement.ValueKind == JsonValueKind.String)
        {
            return responseElement.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    protected override string? ReadFinishReason(JsonElement root)
    {
        return ReadOptionalString(root, "done_reason");
    }

    protected override IReadOnlyDictionary<string, string>? BuildMetadata(JsonElement root, McpInferenceProviderOptions providerOptions)
    {
        _ = providerOptions;

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["providerId"] = ProviderId,
            ["displayName"] = DisplayName
        };

        var model = ReadOptionalString(root, "model");
        if (!string.IsNullOrWhiteSpace(model))
        {
            metadata["model"] = model.Trim();
        }

        CopyDuration(metadata, root, "total_duration", "totalDurationMilliseconds");
        CopyDuration(metadata, root, "load_duration", "loadDurationMilliseconds");
        CopyDuration(metadata, root, "prompt_eval_duration", "promptEvalDurationMilliseconds");
        CopyDuration(metadata, root, "eval_duration", "evalDurationMilliseconds");

        var promptTokens = ReadOptionalInt64(root, "prompt_eval_count");
        if (promptTokens is not null)
        {
            metadata["promptEvalCount"] = promptTokens.Value.ToString(CultureInfo.InvariantCulture);
        }

        var evalTokens = ReadOptionalInt64(root, "eval_count");
        if (evalTokens is not null)
        {
            metadata["evalCount"] = evalTokens.Value.ToString(CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    protected override InferenceUsage? TryParseUsage(JsonElement root)
    {
        var promptTokens = ReadOptionalInt32(root, "prompt_eval_count");
        var outputTokens = ReadOptionalInt32(root, "eval_count");
        if (promptTokens is null && outputTokens is null)
        {
            return null;
        }

        return new InferenceUsage(
            promptTokens,
            outputTokens,
            GetCombinedTokens(promptTokens, outputTokens));
    }

    private static void CopyDuration(
        IDictionary<string, string> metadata,
        JsonElement root,
        string sourceName,
        string targetName)
    {
        var value = ReadOptionalInt64(root, sourceName);
        if (value is null)
        {
            return;
        }

        var milliseconds = Math.Max(0d, value.Value / 1_000_000d);
        metadata[targetName] = milliseconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static int? GetCombinedTokens(int? inputTokens, int? outputTokens)
    {
        if (inputTokens is null && outputTokens is null)
        {
            return null;
        }

        return (inputTokens ?? 0) + (outputTokens ?? 0);
    }
}
