namespace MCPServer.Inference.Abstractions.Models;

public enum InferenceProviderProbeStatus
{
    Disabled = 0,
    NotConfigured = 1,
    Ready = 2,
    Unauthorized = 3,
    Unreachable = 4,
    Timeout = 5,
    Error = 6
}

public sealed record InferenceProviderProbeResult(
    string ProviderId,
    string DisplayName,
    InferenceProviderProbeStatus Status,
    bool Enabled,
    int? HttpStatusCode = null,
    int? ElapsedMilliseconds = null,
    string? Message = null,
    string? Endpoint = null,
    DateTimeOffset? CheckedAtUtc = null)
{
    public static InferenceProviderProbeResult Disabled(
        string providerId,
        string displayName,
        string? message = null)
    {
        return new InferenceProviderProbeResult(
            providerId,
            displayName,
            InferenceProviderProbeStatus.Disabled,
            Enabled: false,
            Message: message,
            CheckedAtUtc: DateTimeOffset.UtcNow);
    }

    public static InferenceProviderProbeResult NotConfigured(
        string providerId,
        string displayName,
        string? message = null)
    {
        return new InferenceProviderProbeResult(
            providerId,
            displayName,
            InferenceProviderProbeStatus.NotConfigured,
            Enabled: false,
            Message: message,
            CheckedAtUtc: DateTimeOffset.UtcNow);
    }

    public static InferenceProviderProbeResult Ready(
        string providerId,
        string displayName,
        int? httpStatusCode,
        int? elapsedMilliseconds,
        string? endpoint = null)
    {
        return new InferenceProviderProbeResult(
            providerId,
            displayName,
            InferenceProviderProbeStatus.Ready,
            Enabled: true,
            HttpStatusCode: httpStatusCode,
            ElapsedMilliseconds: elapsedMilliseconds,
            Endpoint: endpoint,
            CheckedAtUtc: DateTimeOffset.UtcNow);
    }

    public static InferenceProviderProbeResult Unauthorized(
        string providerId,
        string displayName,
        int? httpStatusCode,
        int? elapsedMilliseconds,
        string? message,
        string? endpoint = null)
    {
        return new InferenceProviderProbeResult(
            providerId,
            displayName,
            InferenceProviderProbeStatus.Unauthorized,
            Enabled: true,
            HttpStatusCode: httpStatusCode,
            ElapsedMilliseconds: elapsedMilliseconds,
            Message: message,
            Endpoint: endpoint,
            CheckedAtUtc: DateTimeOffset.UtcNow);
    }

    public static InferenceProviderProbeResult Unreachable(
        string providerId,
        string displayName,
        int? httpStatusCode,
        int? elapsedMilliseconds,
        string? message,
        string? endpoint = null)
    {
        return new InferenceProviderProbeResult(
            providerId,
            displayName,
            InferenceProviderProbeStatus.Unreachable,
            Enabled: true,
            HttpStatusCode: httpStatusCode,
            ElapsedMilliseconds: elapsedMilliseconds,
            Message: message,
            Endpoint: endpoint,
            CheckedAtUtc: DateTimeOffset.UtcNow);
    }

    public static InferenceProviderProbeResult Timeout(
        string providerId,
        string displayName,
        int? elapsedMilliseconds,
        string? message = null,
        string? endpoint = null)
    {
        return new InferenceProviderProbeResult(
            providerId,
            displayName,
            InferenceProviderProbeStatus.Timeout,
            Enabled: true,
            ElapsedMilliseconds: elapsedMilliseconds,
            Message: message,
            Endpoint: endpoint,
            CheckedAtUtc: DateTimeOffset.UtcNow);
    }

    public static InferenceProviderProbeResult Error(
        string providerId,
        string displayName,
        int? elapsedMilliseconds,
        string message,
        string? endpoint = null)
    {
        return new InferenceProviderProbeResult(
            providerId,
            displayName,
            InferenceProviderProbeStatus.Error,
            Enabled: true,
            ElapsedMilliseconds: elapsedMilliseconds,
            Message: message,
            Endpoint: endpoint,
            CheckedAtUtc: DateTimeOffset.UtcNow);
    }
}
