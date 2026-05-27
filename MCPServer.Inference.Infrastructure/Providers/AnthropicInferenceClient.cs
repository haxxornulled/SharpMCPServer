using System.Buffers;
using System.Text;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace MCPServer.Inference.Infrastructure.Providers;

public sealed class AnthropicInferenceClient : ConfiguredInferenceClientBase
{
    public const string ProviderName = "anthropic";

    public AnthropicInferenceClient(
        IHttpClientFactory httpClientFactory,
        McpInferenceOptions options,
        ILogger<AnthropicInferenceClient> logger)
        : base(httpClientFactory, options, logger)
    {
    }

    public override string ProviderId => ProviderName;

    public override string DisplayName => "Anthropic";

    protected override string GetRequestPath() => "messages";

    protected override void ConfigureRequest(HttpRequestMessage request, McpInferenceProviderOptions providerOptions)
    {
        if (!string.IsNullOrWhiteSpace(providerOptions.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("x-api-key", providerOptions.ApiKey.Trim());
        }

        request.Headers.TryAddWithoutValidation(
            "anthropic-version",
            string.IsNullOrWhiteSpace(providerOptions.AnthropicVersion)
                ? "2023-06-01"
                : providerOptions.AnthropicVersion.Trim());
    }

    protected override Fin<InferenceResponse> ParseResponse(string payload, McpInferenceProviderOptions providerOptions)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            var content = ExtractContent(root);
            if (string.IsNullOrWhiteSpace(content))
            {
                return Fin.Fail<InferenceResponse>(Error.New("Anthropic response did not include assistant content."));
            }

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["providerId"] = ProviderId,
                ["displayName"] = DisplayName
            };

            var responseId = ReadOptionalString(root, "id");
            if (!string.IsNullOrWhiteSpace(responseId))
            {
                metadata["responseId"] = responseId;
            }

            var model = ReadOptionalString(root, "model") ?? providerOptions.Model;
            var finishReason = ReadOptionalString(root, "stop_reason", "finish_reason");
            InferenceUsage? usage = null;
            if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
            {
                usage = new InferenceUsage(
                    ReadOptionalInt32(usageElement, "input_tokens"),
                    ReadOptionalInt32(usageElement, "output_tokens"),
                    ReadOptionalInt32(usageElement, "input_tokens") + ReadOptionalInt32(usageElement, "output_tokens"));
            }

            return Fin.Succ(new InferenceResponse(
                ProviderId,
                model,
                content,
                finishReason,
                usage,
                metadata));
        }
        catch (Exception ex)
        {
            return Fin.Fail<InferenceResponse>(Error.New($"Failed to parse Anthropic inference response: {ex.Message}"));
        }
    }

    protected override string BuildRequestJson(InferenceRequest request, McpInferenceProviderOptions providerOptions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", request.Model ?? providerOptions.Model);
            writer.WriteNumber("max_tokens", request.MaxTokens is int maxTokens && maxTokens > 0 ? maxTokens : 1024);

            if (request.Messages.Any(static message => message.Role == InferenceRole.System))
            {
                var systemMessage = string.Join("\n", request.Messages.Where(static message => message.Role == InferenceRole.System).Select(static message => message.Content));
                writer.WriteString("system", systemMessage);
            }

            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            foreach (var message in request.Messages.Where(static message => message.Role != InferenceRole.System))
            {
                writer.WriteStartObject();
                writer.WriteString("role", message.Role == InferenceRole.Assistant ? "assistant" : message.Role == InferenceRole.Tool ? "assistant" : "user");
                writer.WritePropertyName("content");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("type", "text");
                writer.WriteString("text", message.Content);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string ExtractContent(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var contentElement))
        {
            return string.Empty;
        }

        if (contentElement.ValueKind == JsonValueKind.String)
        {
            return contentElement.GetString() ?? string.Empty;
        }

        if (contentElement.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();
            foreach (var item in contentElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("text", out var textProperty) &&
                    textProperty.ValueKind == JsonValueKind.String)
                {
                    builder.Append(textProperty.GetString());
                }
            }

            return builder.ToString();
        }

        return string.Empty;
    }
}
