using System.Text.Json;

namespace MCPServer.AgentRouter.Infrastructure.Persistence;

internal static class AgentRunSnapshotJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string? SerializeMetadata(IReadOnlyDictionary<string, string?>? metadata)
    {
        return metadata switch
        {
            null => null,
            { Count: 0 } => null,
            _ => JsonSerializer.Serialize(metadata, SerializerOptions)
        };
    }

    public static IReadOnlyDictionary<string, string?>? DeserializeMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        var metadata = JsonSerializer.Deserialize<Dictionary<string, string?>>(metadataJson, SerializerOptions);
        return metadata is { Count: > 0 } ? metadata : null;
    }
}
