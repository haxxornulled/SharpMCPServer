namespace MCPServer.Client.Authorization;

public sealed class McpAuthorizationContext
{
    public required Uri Endpoint { get; init; }

    public McpAuthorizationChallenge? Challenge { get; init; }

    public McpProtectedResourceMetadata? ProtectedResourceMetadata { get; init; }

    public IReadOnlyList<McpAuthorizationServerDescriptor> AuthorizationServers { get; init; } = Array.Empty<McpAuthorizationServerDescriptor>();

    public IReadOnlyList<string> RequiredScopes { get; init; } = Array.Empty<string>();
}
