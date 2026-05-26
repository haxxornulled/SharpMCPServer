using System.Text.Json.Serialization;

namespace MCPServer.Domain.Mcp;

public sealed class ResourceSubscriptionRequest
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;
}

public sealed class ResourceUpdatedNotificationParams
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;
}
