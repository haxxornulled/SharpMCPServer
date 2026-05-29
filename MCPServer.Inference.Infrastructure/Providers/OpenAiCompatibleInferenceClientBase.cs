using System.Buffers;
using System.Diagnostics;
using System.Net;
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

    public override async ValueTask<Fin<InferenceResponse>> GenerateAsync(
        InferenceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetProviderOptions(out var providerOptions))
            {
                return Fin.Fail<InferenceResponse>(Error.New($"Inference provider '{ProviderId}' is not configured."));
            }

            if (!providerOptions.Enabled)
            {
                return Fin.Fail<InferenceResponse>(Error.New($"Inference provider '{ProviderId}' is disabled."));
            }

            var primaryResult = await GenerateOnceAsync(request, providerOptions, cancellationToken).ConfigureAwait(false);
            if (primaryResult.IsSucc || !ShouldTryModelDiscovery(request, primaryResult))
            {
                return primaryResult;
            }

            var discoveredModel = await TryResolveFallbackModelAsync(providerOptions, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(discoveredModel) ||
                string.Equals(discoveredModel, providerOptions.Model, StringComparison.OrdinalIgnoreCase))
            {
                return primaryResult;
            }

            Logger.LogWarning(
                "Inference provider {ProviderId} rejected configured model {ConfiguredModel}; retrying with discovered model {DiscoveredModel}.",
                ProviderId,
                providerOptions.Model,
                discoveredModel);

            var retryRequest = request with { Model = discoveredModel };
            return await GenerateOnceAsync(retryRequest, providerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fin.Fail<InferenceResponse>(Error.New($"Inference provider '{ProviderId}' failed: {ex.Message}"));
        }
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
            var model = string.IsNullOrWhiteSpace(request.Model)
                ? providerOptions.Model
                : request.Model.Trim();

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
                    writer.WriteString("name", message.Name);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            var effectiveMaxTokens = request.MaxTokens is int requestMaxTokens && requestMaxTokens > 0
                ? requestMaxTokens
                : providerOptions.MaxTokens;
            if (effectiveMaxTokens is int maxTokens && maxTokens > 0)
            {
                writer.WriteNumber("max_tokens", maxTokens);
            }

            var effectiveTemperature = request.Temperature ?? providerOptions.Temperature;
            if (effectiveTemperature is double temperature)
            {
                writer.WriteNumber("temperature", temperature);
            }

            if (providerOptions.TopP is double topP)
            {
                writer.WriteNumber("top_p", topP);
            }

            if (providerOptions.Seed is int seed)
            {
                writer.WriteNumber("seed", seed);
            }

            WriteAdditionalRequestFields(writer, request, providerOptions);

            writer.WriteBoolean("stream", false);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    protected override string GetRequestPath() => "chat/completions";

    protected virtual void WriteAdditionalRequestFields(
        Utf8JsonWriter writer,
        InferenceRequest request,
        McpInferenceProviderOptions providerOptions)
    {
    }

    private async ValueTask<Fin<InferenceResponse>> GenerateOnceAsync(
        InferenceRequest request,
        McpInferenceProviderOptions providerOptions,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildRequestUri(providerOptions.BaseAddress, GetRequestPath());
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
        var startedAt = Stopwatch.GetTimestamp();

        ConfigureRequest(httpRequest, providerOptions);
        httpRequest.Content = new StringContent(BuildRequestJson(request, providerOptions), Encoding.UTF8, "application/json");

        var httpClient = HttpClientFactory.CreateClient(GetHttpClientName(providerOptions));
        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return Fin.Fail<InferenceResponse>(Error.New(BuildHttpFailureMessage(response.StatusCode, payload)));
        }

        var parseResult = ParseResponse(payload, providerOptions);
        return parseResult.Match<Fin<InferenceResponse>>(
            Succ: value => Fin.Succ(AttachPerformanceMetadata(value, Stopwatch.GetElapsedTime(startedAt))),
            Fail: static error => Fin.Fail<InferenceResponse>(error));
    }

    private async ValueTask<string?> TryResolveFallbackModelAsync(
        McpInferenceProviderOptions providerOptions,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildRequestUri(providerOptions.BaseAddress, GetProbePath());
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);

        ConfigureRequest(httpRequest, providerOptions);

        var httpClient = HttpClientFactory.CreateClient(GetHttpClientName(providerOptions));
        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (TryReadModelIdentifier(root, out var modelId))
            {
                return modelId;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool ShouldTryModelDiscovery(
        InferenceRequest request,
        Fin<InferenceResponse> result)
    {
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            return false;
        }

        return result.Match(
            Succ: static _ => false,
            Fail: static error => IsMissingModelFailure(error.Message));
    }

    private static bool IsMissingModelFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("model", StringComparison.OrdinalIgnoreCase) &&
            (message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("unknown model", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("no such model", StringComparison.OrdinalIgnoreCase));
    }

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

    protected static bool TryReadModelIdentifier(
        JsonElement root,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? modelId)
    {
        modelId = null;

        foreach (var listPropertyName in new[] { "data", "models" })
        {
            if (!root.TryGetProperty(listPropertyName, out var listElement) || listElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in listElement.EnumerateArray())
            {
                var candidate = ReadOptionalString(item, "id", "key", "model", "name");
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    modelId = candidate.Trim();
                    return true;
                }
            }
        }

        return false;
    }

    protected static long? ReadOptionalInt64(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var longValue))
            {
                return longValue;
            }

            if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out longValue))
            {
                return longValue;
            }
        }

        return null;
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
