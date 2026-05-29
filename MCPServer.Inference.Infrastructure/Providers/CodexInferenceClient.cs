using MCPServer.Inference.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace MCPServer.Inference.Infrastructure.Providers;

public sealed class CodexInferenceClient : OpenAiCompatibleInferenceClientBase
{
    public const string ProviderName = "codex";

    public CodexInferenceClient(
        IHttpClientFactory httpClientFactory,
        McpInferenceOptions options,
        ILogger<CodexInferenceClient> logger)
        : base(httpClientFactory, options, logger)
    {
    }

    public override string ProviderId => ProviderName;

    public override string DisplayName => "Codex";
}
