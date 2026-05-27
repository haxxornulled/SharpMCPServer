using System.Buffers;
using System.Text;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace MCPServer.Inference.Infrastructure.Providers;

public abstract class OpenAiCompatibleInferenceClientBase : ConfiguredInferenceClientBase
{
    protected OpenAiCompatibleInferenceClientBase(
        IHttpClientFactory httpClientFactory,
        McpInferenceOptions options,
        ILogger logger)
        : base(httpClientFactory, options, logger)
    {
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
                return Fin.Fail<InferenceResponse>(Error.New("Inference provider response did not include assistant content."));
            }

            var metadata = BuildMetadata(root, providerOptions);
            var model = ReadOptionalString(root, "model") ?? providerOptions.Model;
            var finishReason = ReadFinishReason(root);
            var usage = TryParseUsage(root);

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
            return Fin.Fail<InferenceResponse>(Error.New($"Failed to parse inference provider response: {ex.Message}"));
        }
    }

    protected override string BuildRequestJson(InferenceRequest request, McpInferenceProviderOptions providerOptions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", request.Model ?? providerOptions.Model);
            writer.WritePropertyName("messages");
            writer.WriteStartArray();

            foreach (var message in request.Messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", ToProviderRole(message.Role));
                writer.WriteString("content", message.Content);
                if (!string.IsNullOrWhiteSpace(message.Name))
                {
                    writer.WriteString("name", message.Name);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (request.MaxTokens is int maxTokens && maxTokens > 0)
            {
                writer.WriteNumber("max_tokens", maxTokens);
            }

            if (request.Temperature is double temperature)
            {
                writer.WriteNumber("temperature", temperature);
            }

            writer.WriteBoolean("stream", false);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    protected override string GetRequestPath() => "chat/completions";

    protected virtual string ExtractContent(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choicesElement) &&
            choicesElement.ValueKind == JsonValueKind.Array &&
            choicesElement.GetArrayLength() > 0)
        {
            var choice = choicesElement[0];
            if (choice.TryGetProperty("message", out var messageElement))
            {
                var content = ReadContentElement(messageElement);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return content;
                }
            }

            var text = ReadOptionalString(choice, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (root.TryGetProperty("content", out var contentElement))
        {
            var content = ReadContentElement(contentElement);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return string.Empty;
    }

    protected virtual string? ReadFinishReason(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choicesElement) &&
            choicesElement.ValueKind == JsonValueKind.Array &&
            choicesElement.GetArrayLength() > 0)
        {
            var choice = choicesElement[0];
            return ReadOptionalString(choice, "finish_reason", "stop_reason");
        }

        return null;
    }

    protected virtual IReadOnlyDictionary<string, string>? BuildMetadata(JsonElement root, McpInferenceProviderOptions providerOptions)
    {
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

        var responseObject = ReadOptionalString(root, "object");
        if (!string.IsNullOrWhiteSpace(responseObject))
        {
            metadata["responseObject"] = responseObject;
        }

        return metadata;
    }

    protected virtual InferenceUsage? TryParseUsage(JsonElement root)
    {
        var usageResult = ParseUsage(root);
        return usageResult.Match(
            Succ: static value => value,
            Fail: static _ => null!);
    }

    protected static string ReadContentElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("content", out var nestedContentProperty))
        {
            return ReadContentElement(nestedContentProperty);
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();
            foreach (var child in element.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.String)
                {
                    builder.Append(child.GetString());
                    continue;
                }

                if (child.ValueKind == JsonValueKind.Object)
                {
                    if (child.TryGetProperty("text", out var textProperty) && textProperty.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(textProperty.GetString());
                        continue;
                    }

                    if (child.TryGetProperty("content", out var childContentProperty))
                    {
                        builder.Append(ReadContentElement(childContentProperty));
                    }
                }
            }

            return builder.ToString();
        }

        return string.Empty;
    }

    protected static string ToProviderRole(InferenceRole role)
    {
        return role switch
        {
            InferenceRole.System => "system",
            InferenceRole.Assistant => "assistant",
            InferenceRole.Tool => "tool",
            _ => "user"
        };
    }
}
