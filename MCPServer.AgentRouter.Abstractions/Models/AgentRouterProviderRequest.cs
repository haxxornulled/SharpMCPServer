using MCPServer.AgentRouter.Abstractions;

namespace MCPServer.AgentRouter.Abstractions.Models;

public readonly record struct AgentRouterProviderRequest(
    string? RouterName,
    IReadOnlyDictionary<string, string?>? Metadata)
{
    public static AgentRouterProviderRequest Default => new(
        RouterName: AgentRouterNames.Default,
        Metadata: AgentRouterMetadata.Empty);

    public IReadOnlyDictionary<string, string?> MetadataOrEmpty => Metadata ?? AgentRouterMetadata.Empty;
}
