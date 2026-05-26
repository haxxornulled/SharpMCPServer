using System.Text.Json.Serialization;

namespace MCPServer.Client.Authorization;

public sealed class McpAuthorizationServerMetadata
{
    [JsonPropertyName("issuer")]
    public string? Issuer { get; init; }

    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint { get; init; }

    [JsonPropertyName("token_endpoint")]
    public string? TokenEndpoint { get; init; }

    [JsonPropertyName("registration_endpoint")]
    public string? RegistrationEndpoint { get; init; }

    [JsonPropertyName("client_id_metadata_document_supported")]
    public bool? ClientIdMetadataDocumentSupported { get; init; }

    [JsonPropertyName("code_challenge_methods_supported")]
    public string[]? CodeChallengeMethodsSupported { get; init; }
}
