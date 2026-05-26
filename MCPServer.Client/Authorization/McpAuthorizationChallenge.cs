namespace MCPServer.Client.Authorization;

public sealed class McpAuthorizationChallenge
{
    public string Scheme { get; init; } = string.Empty;

    public string? Realm { get; init; }

    public Uri? ResourceMetadataUri { get; init; }

    public string? Scope { get; init; }
}
