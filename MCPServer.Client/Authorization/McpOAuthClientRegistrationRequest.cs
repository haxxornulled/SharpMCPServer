using System.Text.Json.Serialization;

namespace MCPServer.Client.Authorization;

public sealed class McpOAuthClientRegistrationRequest
{
    [JsonPropertyName("client_name")]
    public string ClientName { get; init; } = string.Empty;

    [JsonPropertyName("client_uri")]
    public string? ClientUri { get; init; }

    [JsonPropertyName("application_type")]
    public string ApplicationType { get; init; } = "native";

    [JsonPropertyName("redirect_uris")]
    public string[] RedirectUris { get; init; } = Array.Empty<string>();

    [JsonPropertyName("grant_types")]
    public string[] GrantTypes { get; init; } = ["authorization_code", "refresh_token"];

    [JsonPropertyName("response_types")]
    public string[] ResponseTypes { get; init; } = ["code"];

    [JsonPropertyName("token_endpoint_auth_method")]
    public string TokenEndpointAuthMethod { get; init; } = "none";
}
