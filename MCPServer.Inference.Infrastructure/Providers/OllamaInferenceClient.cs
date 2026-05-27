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
}
