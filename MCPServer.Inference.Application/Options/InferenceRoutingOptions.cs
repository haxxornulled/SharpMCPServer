using MCPServer.Inference.Abstractions.Models;

namespace MCPServer.Inference.Application.Options;

public sealed class InferenceRoutingOptions
{
    public static InferenceRoutingOptions Default => new();

    public InferenceRoutingStrategy DefaultStrategy { get; set; } = InferenceRoutingStrategy.PrimaryThenFallback;

    public int MaxConcurrentRequestsPerProvider { get; set; } = 2;

    public int MaxFanOutCandidates { get; set; } = 4;

    public void Validate()
    {
        if (MaxConcurrentRequestsPerProvider <= 0)
        {
            throw new InvalidOperationException("InferenceRouting:MaxConcurrentRequestsPerProvider must be greater than zero.");
        }

        if (MaxFanOutCandidates <= 0)
        {
            throw new InvalidOperationException("InferenceRouting:MaxFanOutCandidates must be greater than zero.");
        }
    }
}
