using MCPServer.Inference.Abstractions.Models;

namespace MCPServer.Inference.Application.Options;

public sealed class InferenceRoutingOptions
{
    public static InferenceRoutingOptions Default => new();

    public InferenceRoutingStrategy DefaultStrategy { get; set; } = InferenceRoutingStrategy.PrimaryThenFallback;

    public int MaxConcurrentRequestsPerProvider { get; set; } = 2;

    public int MaxFanOutCandidates { get; set; } = 4;

    public int TandemCandidateCount { get; set; } = 2;

    public bool TandemValidationEnabled { get; set; }

    public string TandemValidationProviderId { get; set; } = string.Empty;

    public string TandemValidationModel { get; set; } = string.Empty;

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

        if (TandemCandidateCount < 2)
        {
            throw new InvalidOperationException("InferenceRouting:TandemCandidateCount must be at least two.");
        }

        if (TandemValidationEnabled && string.IsNullOrWhiteSpace(TandemValidationProviderId))
        {
            throw new InvalidOperationException("InferenceRouting:TandemValidationProviderId must be configured when tandem validation is enabled.");
        }
    }
}
