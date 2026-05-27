using LanguageExt;
using MCPServer.Inference.Abstractions.Models;

namespace MCPServer.Inference.Abstractions.Interfaces;

public interface IInferenceRouter
{
    ValueTask<Fin<InferenceResponse>> GenerateAsync(
        InferenceRequest request,
        CancellationToken cancellationToken);
}
