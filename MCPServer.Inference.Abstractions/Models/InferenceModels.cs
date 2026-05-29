namespace MCPServer.Inference.Abstractions.Models;

public enum InferenceRole
{
    System = 0,
    User = 1,
    Assistant = 2,
    Tool = 3
}

public enum InferenceRoutingStrategy
{
    PrimaryOnly = 0,
    PrimaryThenFallback = 1,
    FanOutCompare = 2,
    TandemValidate = 3,
    SecondOpinion = 4
}

public sealed record InferenceMessage(
    InferenceRole Role,
    string Content,
    string? Name = null);

public sealed record InferenceRequest(
    IReadOnlyList<InferenceMessage> Messages,
    string? Model = null,
    int? MaxTokens = null,
    double? Temperature = null,
    InferenceRoutingHint? RoutingHint = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record InferenceResponse(
    string ProviderId,
    string Model,
    string Content,
    string? FinishReason = null,
    InferenceUsage? Usage = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record InferenceUsage(
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens);

public sealed record InferenceRoutingHint(
    InferenceRoutingStrategy Strategy,
    string? PreferredProviderId = null,
    IReadOnlyList<string>? FallbackProviderIds = null);

public sealed record InferenceProviderDescriptor(
    string ProviderId,
    string DisplayName,
    bool Enabled,
    bool SupportsStreaming,
    int RoutingPriority = 0);
