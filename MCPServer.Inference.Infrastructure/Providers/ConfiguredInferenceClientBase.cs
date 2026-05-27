using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace MCPServer.Inference.Infrastructure.Providers;

public abstract class ConfiguredInferenceClientBase : IInferenceClient
{
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly McpInferenceOptions Options;
    protected readonly ILogger Logger;

    protected ConfiguredInferenceClientBase(
        IHttpClientFactory httpClientFactory,
        McpInferenceOptions options,
        ILogger logger)
    {
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract string ProviderId { get; }

    public abstract string DisplayName { get; }

    public virtual bool SupportsStreaming => false;

    public InferenceProviderDescriptor Descriptor
    {
        get
        {
            var enabled = TryGetProviderOptions(out var providerOptions) && providerOptions.Enabled;
            return new InferenceProviderDescriptor(ProviderId, DisplayName, enabled, SupportsStreaming);
        }
    }

    public virtual async ValueTask<InferenceProviderProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetProviderOptions(out var providerOptions))
            {
                return InferenceProviderProbeResult.NotConfigured(
                    ProviderId,
                    DisplayName,
                    $"Inference provider '{ProviderId}' is not configured.");
            }

            if (!providerOptions.Enabled)
            {
                return InferenceProviderProbeResult.Disabled(
                    ProviderId,
                    DisplayName,
                    $"Inference provider '{ProviderId}' is disabled.");
            }

            var requestUri = BuildRequestUri(providerOptions.BaseAddress, GetProbePath());
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);

            ConfigureRequest(httpRequest, providerOptions);

            var httpClient = HttpClientFactory.CreateClient(GetHttpClientName(providerOptions));
            var startedAt = Stopwatch.GetTimestamp();
            using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var elapsedMilliseconds = (int)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                return InferenceProviderProbeResult.Ready(
                    ProviderId,
                    DisplayName,
                    (int)response.StatusCode,
                    elapsedMilliseconds,
                    requestUri.ToString());
            }

            var payload = await ReadProbePayloadAsync(response, cancellationToken).ConfigureAwait(false);
            var message = BuildHttpFailureMessage(response.StatusCode, payload);

            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? InferenceProviderProbeResult.Unauthorized(
                    ProviderId,
                    DisplayName,
                    (int)response.StatusCode,
                    elapsedMilliseconds,
                    message,
                    requestUri.ToString())
                : InferenceProviderProbeResult.Unreachable(
                    ProviderId,
                    DisplayName,
                    (int)response.StatusCode,
                    elapsedMilliseconds,
                    message,
                    requestUri.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return InferenceProviderProbeResult.Error(
                ProviderId,
                DisplayName,
                null,
                $"Inference provider '{ProviderId}' probe failed: {ex.Message}");
        }
    }

    public virtual async ValueTask<Fin<InferenceResponse>> GenerateAsync(
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

            var requestUri = BuildRequestUri(providerOptions.BaseAddress, GetRequestPath());
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);

            ConfigureRequest(httpRequest, providerOptions);
            httpRequest.Content = new StringContent(BuildRequestJson(request, providerOptions), System.Text.Encoding.UTF8, "application/json");

            var httpClient = HttpClientFactory.CreateClient(GetHttpClientName(providerOptions));
            using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Fin.Fail<InferenceResponse>(Error.New(BuildHttpFailureMessage(response.StatusCode, payload)));
            }

            return ParseResponse(payload, providerOptions);
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

    protected abstract string GetRequestPath();

    protected virtual string GetProbePath() => "models";

    protected virtual void ConfigureRequest(HttpRequestMessage request, McpInferenceProviderOptions providerOptions)
    {
        if (!string.IsNullOrWhiteSpace(providerOptions.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerOptions.ApiKey.Trim());
        }
    }

    protected abstract Fin<InferenceResponse> ParseResponse(string payload, McpInferenceProviderOptions providerOptions);

    protected static Uri BuildRequestUri(string baseAddress, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseAddress))
        {
            throw new InvalidOperationException("Inference provider base address is required.");
        }

        var normalizedBaseAddress = baseAddress.Trim();
        if (!Uri.TryCreate(normalizedBaseAddress, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Inference provider base address '{baseAddress}' is not a valid absolute URI.");
        }

        return new Uri(baseUri, relativePath.TrimStart('/'));
    }

    protected static string BuildHttpFailureMessage(HttpStatusCode statusCode, string payload)
    {
        var snippet = payload.Length <= 512 ? payload : payload[..512];
        return $"Inference provider returned HTTP {(int)statusCode} ({statusCode}): {snippet}";
    }

    private static async ValueTask<string> ReadProbePayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return string.Empty;
        }
    }

    protected string GetHttpClientName(McpInferenceProviderOptions providerOptions)
    {
        return string.IsNullOrWhiteSpace(providerOptions.HttpClientName)
            ? ProviderId
            : providerOptions.HttpClientName.Trim();
    }

    protected bool TryGetProviderOptions([MaybeNullWhen(false)] out McpInferenceProviderOptions providerOptions)
    {
        if (Options.Providers.TryGetValue(ProviderId, out providerOptions) && providerOptions is not null)
        {
            return true;
        }

        providerOptions = new McpInferenceProviderOptions();
        Logger.LogDebug("Inference provider {ProviderId} is not configured.", ProviderId);
        return false;
    }

    protected static Fin<InferenceUsage> ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usageElement) || usageElement.ValueKind != JsonValueKind.Object)
        {
            return Fin.Fail<InferenceUsage>(Error.New("Inference response did not include usage metadata."));
        }

        var inputTokens = ReadOptionalInt32(usageElement, "prompt_tokens", "input_tokens");
        var outputTokens = ReadOptionalInt32(usageElement, "completion_tokens", "output_tokens");
        var totalTokens = ReadOptionalInt32(usageElement, "total_tokens");
        return Fin.Succ(new InferenceUsage(inputTokens, outputTokens, totalTokens));
    }

    protected abstract string BuildRequestJson(InferenceRequest request, McpInferenceProviderOptions providerOptions);

    protected static string? ReadOptionalString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    protected static int? ReadOptionalInt32(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out intValue))
            {
                return intValue;
            }
        }

        return null;
    }
}
