namespace MCPServer.Client.Authorization;

public sealed class McpAuthorizationServerDescriptor
{
    public required Uri MetadataUri { get; init; }

    public required Uri Issuer { get; init; }

    public required Uri AuthorizationEndpoint { get; init; }

    public required Uri TokenEndpoint { get; init; }

    public Uri? RegistrationEndpoint { get; init; }

    public bool ClientIdMetadataDocumentSupported { get; init; }

    public IReadOnlyList<string> CodeChallengeMethodsSupported { get; init; } = Array.Empty<string>();

    public McpAuthorizationServerDiscoverySource DiscoverySource { get; init; }

    public bool SupportsDynamicClientRegistration => RegistrationEndpoint is not null;

    public bool SupportsPkce
    {
        get
        {
            foreach (var codeChallengeMethod in CodeChallengeMethodsSupported)
            {
                if (string.Equals(codeChallengeMethod, "S256", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
