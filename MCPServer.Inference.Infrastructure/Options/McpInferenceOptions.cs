namespace MCPServer.Inference.Infrastructure.Options;

public sealed class McpInferenceOptions
{
    public const string ConfigurationSectionName = "McpInference";

    public Dictionary<string, McpInferenceProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        foreach (var (providerId, providerOptions) in Providers)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new InvalidOperationException("McpInference: provider identifiers must not be blank.");
            }

            providerOptions.Validate(providerId);
        }
    }
}

public sealed class McpInferenceProviderOptions
{
    public bool Enabled { get; set; }

    public string BaseAddress { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string HttpClientName { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string AnthropicVersion { get; set; } = "2023-06-01";

    public int RoutingPriority { get; set; }

    public int? MaxTokens { get; set; }

    public double? Temperature { get; set; }

    public double? TopP { get; set; }

    public int? TopK { get; set; }

    public double? RepeatPenalty { get; set; }

    public int? Seed { get; set; }

    public int? ContextLength { get; set; }

    public string KeepAlive { get; set; } = string.Empty;

    public void Validate(string providerId)
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(BaseAddress))
        {
            throw new InvalidOperationException($"McpInference: provider '{providerId}' requires BaseAddress when enabled.");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new InvalidOperationException($"McpInference: provider '{providerId}' requires Model when enabled.");
        }

        if (RequiresApiKey(providerId) && string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException($"McpInference: provider '{providerId}' requires ApiKey when enabled.");
        }

        if (MaxTokens is not null && MaxTokens <= 0)
        {
            throw new InvalidOperationException($"McpInference: provider '{providerId}' requires MaxTokens to be greater than zero when configured.");
        }

        if (Temperature is not null && Temperature < 0)
        {
            throw new InvalidOperationException($"McpInference: provider '{providerId}' requires Temperature to be zero or greater when configured.");
        }

        if (TopP is not null && (TopP < 0 || TopP > 1))
        {
            throw new InvalidOperationException($"McpInference: provider '{providerId}' requires TopP to be between zero and one when configured.");
        }

        if (TopK is not null && TopK <= 0)
        {
            throw new InvalidOperationException($"McpInference: provider '{providerId}' requires TopK to be greater than zero when configured.");
        }

        if (RepeatPenalty is not null && RepeatPenalty <= 0)
        {
            throw new InvalidOperationException($"McpInference: provider '{providerId}' requires RepeatPenalty to be greater than zero when configured.");
        }

        if (ContextLength is not null && ContextLength <= 0)
        {
            throw new InvalidOperationException($"McpInference: provider '{providerId}' requires ContextLength to be greater than zero when configured.");
        }
    }

    private static bool RequiresApiKey(string providerId)
    {
        return string.Equals(providerId, "anthropic", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerId, "openai", StringComparison.OrdinalIgnoreCase);
    }
}
