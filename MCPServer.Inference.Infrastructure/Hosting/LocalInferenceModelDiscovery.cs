using System.Globalization;
using System.Text.Json;

namespace MCPServer.Inference.Infrastructure.Hosting;

internal sealed record LocalInferenceModelDescriptor(
    string ModelKey,
    string DisplayName,
    long? SizeBytes);

internal static class LocalInferenceModelDiscovery
{
    public static IReadOnlyList<LocalInferenceModelDescriptor> ParseLmStudioModels(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var models = new List<LocalInferenceModelDescriptor>();
        CollectModelDescriptors(document.RootElement, models);
        return models;
    }

    public static string? SelectPreferredModel(
        IReadOnlyList<LocalInferenceModelDescriptor> models,
        string? requestedModel)
    {
        if (models.Count == 0)
        {
            return null;
        }

        var normalizedRequestedModel = NormalizeModelName(requestedModel);
        if (!string.IsNullOrWhiteSpace(normalizedRequestedModel))
        {
            var exactMatch = models.FirstOrDefault(model =>
                NamesMatch(model.ModelKey, normalizedRequestedModel) ||
                NamesMatch(model.DisplayName, normalizedRequestedModel));

            if (exactMatch is not null)
            {
                return exactMatch.ModelKey;
            }
        }

        return models
            .OrderBy(model => model.SizeBytes ?? long.MaxValue)
            .ThenBy(model => model.ModelKey, StringComparer.OrdinalIgnoreCase)
            .First()
            .ModelKey;
    }

    private static void CollectModelDescriptors(
        JsonElement element,
        ICollection<LocalInferenceModelDescriptor> models)
    {
        if (TryReadModelDescriptor(element, out var descriptor))
        {
            models.Add(descriptor);
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectModelDescriptors(child, models);
            }
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            {
                CollectModelDescriptors(property.Value, models);
            }
        }
    }

    private static bool TryReadModelDescriptor(
        JsonElement element,
        out LocalInferenceModelDescriptor descriptor)
    {
        descriptor = null!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var modelKey = ReadString(element, "modelKey", "model_key", "key", "name");
        var displayName = ReadString(element, "displayName", "display_name", "title", "name");
        var sizeBytes = ReadInt64(element, "sizeBytes", "size_bytes", "size");

        if (string.IsNullOrWhiteSpace(modelKey) && string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        modelKey = string.IsNullOrWhiteSpace(modelKey)
            ? displayName!.Trim()
            : modelKey.Trim();

        displayName = string.IsNullOrWhiteSpace(displayName)
            ? modelKey
            : displayName.Trim();

        descriptor = new LocalInferenceModelDescriptor(modelKey, displayName, sizeBytes);
        return true;
    }

    private static string? ReadString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static long? ReadInt64(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
            {
                return value;
            }

            if (property.ValueKind == JsonValueKind.String &&
                long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool NamesMatch(string? left, string right)
    {
        var normalizedLeft = NormalizeModelName(left);
        return string.Equals(normalizedLeft, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeModelName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.EndsWith(":latest", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^":latest".Length];
        }

        return normalized;
    }
}
