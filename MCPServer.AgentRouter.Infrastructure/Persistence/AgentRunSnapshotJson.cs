using System.Text;
using System.Text.Json;

namespace MCPServer.AgentRouter.Infrastructure.Persistence;

internal static class AgentRunSnapshotJson
{
    public static string? SerializeMetadata(IReadOnlyDictionary<string, string?>? metadata)
    {
        if (metadata is not { Count: > 0 })
        {
            return null;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            foreach (var (key, value) in metadata)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("Agent run metadata keys must be non-empty strings.");
                }

                writer.WritePropertyName(key);

                if (value is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    writer.WriteStringValue(value);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static IReadOnlyDictionary<string, string?>? DeserializeMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(metadataJson));

        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new InvalidOperationException("Agent run metadata JSON must be an object.");
        }

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal);

        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndObject)
            {
                return metadata.Count > 0 ? metadata : null;
            }

            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                throw new InvalidOperationException("Agent run metadata JSON contains an invalid token.");
            }

            var key = reader.GetString();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Agent run metadata JSON contains an empty key.");
            }

            if (!reader.Read())
            {
                throw new InvalidOperationException("Agent run metadata JSON ended before a value was read.");
            }

            metadata[key] = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Null => null,
                _ => throw new InvalidOperationException(
                    $"Agent run metadata value for key '{key}' must be a string or null.")
            };
        }

        throw new InvalidOperationException("Agent run metadata JSON ended before the object was closed.");
    }
}
