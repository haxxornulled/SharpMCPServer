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
    }
}
