using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public static class McpJsonSchemaDialect
{
    public const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";
    private const string Draft202012WithFragment = Draft202012 + "#";

    public static bool TryValidateSupportedDialect(JsonElement schema, out string errorMessage)
    {
        if (schema is not { ValueKind: JsonValueKind.Object })
        {
            errorMessage = "JSON Schema must be an object.";
            return false;
        }

        if (!schema.TryGetProperty("$schema"u8, out var dialectElement))
        {
            errorMessage = string.Empty;
            return true;
        }

        if (dialectElement is not { ValueKind: JsonValueKind.String })
        {
            errorMessage = "JSON Schema $schema must be a string when present.";
            return false;
        }

        if (dialectElement.GetString() is not { Length: > 0 } dialect || string.IsNullOrWhiteSpace(dialect))
        {
            errorMessage = "JSON Schema $schema must not be empty.";
            return false;
        }

        if (IsSupported(dialect))
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = $"JSON Schema dialect '{dialect}' is not supported. Supported dialect: {Draft202012}.";
        return false;
    }

    private static bool IsSupported(string dialect)
    {
        return dialect is Draft202012 or Draft202012WithFragment;
    }
}
