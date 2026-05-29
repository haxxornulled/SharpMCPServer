using System.Text.Json;
using MCPServer.Inference.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace MCPServer.Inference.Infrastructure.Providers;

public sealed class LmStudioInferenceClient : OpenAiCompatibleInferenceClientBase
{
    public const string ProviderName = "lmstudio";

    public LmStudioInferenceClient(
        IHttpClientFactory httpClientFactory,
        McpInferenceOptions options,
        ILogger<LmStudioInferenceClient> logger)
        : base(httpClientFactory, options, logger)
    {
    }

    public override string ProviderId => ProviderName;

    public override string DisplayName => "LM Studio";

    protected override void WriteAdditionalRequestFields(
        Utf8JsonWriter writer,
        MCPServer.Inference.Abstractions.Models.InferenceRequest request,
        McpInferenceProviderOptions providerOptions)
    {
        _ = request;

        if (providerOptions.TopK is int topK && topK > 0)
        {
            writer.WriteNumber("top_k", topK);
        }

        if (providerOptions.RepeatPenalty is double repeatPenalty)
        {
            writer.WriteNumber("repeat_penalty", repeatPenalty);
        }
    }
}
