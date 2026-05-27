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
}
