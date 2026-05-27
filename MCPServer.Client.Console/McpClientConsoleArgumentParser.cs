using System.Buffers;
using System.Text.Json;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleArgumentParser
{
    public static JsonDocument? ParseArguments(string? json, out JsonElement? arguments, out string? error)
    {
        arguments = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                error = "--arguments must be a JSON object.";
                return null;
            }

            arguments = document.RootElement.Clone();
            return document;
        }
        catch (JsonException ex)
        {
            error = $"--arguments is not valid JSON: {ex.Message}";
            return null;
        }
    }

    public static JsonDocument? BuildProbeArguments(
        ConsoleOptions options,
        string toolName,
        out JsonElement? arguments,
        out string? error)
    {
        arguments = null;
        error = null;

        if (!options.ProbeInferenceProviders && options.ProbeInferenceProvidersTimeoutMilliseconds is null)
        {
            return null;
        }

        if (!string.Equals(toolName, "inference.providers.list", StringComparison.OrdinalIgnoreCase))
        {
            error = "--probe and --probe-timeout-ms can only be used with inference.providers.list.";
            return null;
        }

        if (!string.IsNullOrWhiteSpace(options.ToolArgumentsJson))
        {
            error = "--probe cannot be combined with --arguments.";
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("probe", true);

            if (options.ProbeInferenceProvidersTimeoutMilliseconds is int timeoutMilliseconds)
            {
                writer.WriteNumber("probeTimeoutMilliseconds", timeoutMilliseconds);
            }

            writer.WriteEndObject();
        }

        var document = JsonDocument.Parse(buffer.WrittenMemory.ToArray());
        arguments = document.RootElement.Clone();
        return document;
    }

    public static JsonDocument? BuildGenerateArguments(
        ConsoleOptions options,
        string toolName,
        JsonElement? suppliedArguments,
        out JsonElement? arguments,
        out string? error)
    {
        arguments = null;
        error = null;

        if (string.IsNullOrWhiteSpace(options.InferenceProviderId))
        {
            return null;
        }

        if (!string.Equals(toolName, "inference.generate", StringComparison.OrdinalIgnoreCase))
        {
            error = "--provider can only be used with inference.generate.";
            return null;
        }

        if (suppliedArguments is not { ValueKind: JsonValueKind.Object } suppliedObject)
        {
            error = "--provider requires --arguments to supply the inference.generate payload.";
            return null;
        }

        var providerId = options.InferenceProviderId.Trim();
        if (TryReadOptionalString(suppliedObject, "providerId"u8) is { } existingProviderId)
        {
            if (!string.Equals(existingProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                error = $"--provider conflicts with providerId '{existingProviderId}' in --arguments.";
                return null;
            }

            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("providerId", providerId);

            foreach (var property in suppliedObject.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        var document = JsonDocument.Parse(buffer.WrittenMemory.ToArray());
        arguments = document.RootElement.Clone();
        return document;
    }

    private static string? TryReadOptionalString(JsonElement element, ReadOnlySpan<byte> propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }
}
