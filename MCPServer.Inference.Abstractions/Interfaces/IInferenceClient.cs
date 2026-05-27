using LanguageExt;
using MCPServer.Inference.Abstractions.Models;

namespace MCPServer.Inference.Abstractions.Interfaces;

public interface IInferenceClient
{
    string ProviderId { get; }

    InferenceProviderDescriptor Descriptor { get; }

    ValueTask<InferenceProviderProbeResult> ProbeAsync(CancellationToken cancellationToken);

    ValueTask<Fin<InferenceResponse>> GenerateAsync(
        InferenceRequest request,
        CancellationToken cancellationToken);
}
