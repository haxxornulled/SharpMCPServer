using System.Text.Json.Serialization;

namespace MCPServer.Client.Authorization;

public sealed class McpOAuthClientRegistrationResponse
{
    [JsonPropertyName("client_id")]
    public string? ClientId { get; init; }

    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; init; }

    [JsonPropertyName("client_id_issued_at")]
    public long? ClientIdIssuedAt { get; init; }

    [JsonPropertyName("registration_client_uri")]
    public string? RegistrationClientUri { get; init; }

    [JsonPropertyName("registration_access_token")]
    public string? RegistrationAccessToken { get; init; }
}
