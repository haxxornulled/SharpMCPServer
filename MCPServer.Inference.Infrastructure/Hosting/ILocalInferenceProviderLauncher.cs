using MCPServer.Inference.Infrastructure.Options;

namespace MCPServer.Inference.Infrastructure.Hosting;

public interface ILocalInferenceProviderLauncher
{
    ValueTask<ILocalInferenceProviderHandle?> StartAsync(
        string providerId,
        McpInferenceProviderOptions providerOptions,
        CancellationToken cancellationToken);
}
