using System.Text.Json;

namespace MCPServer.Tools.Inference;

internal static class InferenceToolSchemas
{
    public static JsonElement CreateGenerateInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["prompt"],
      "properties": {
        "prompt": { "type": "string", "minLength": 1 },
        "systemPrompt": { "type": "string" },
        "providerId": { "type": "string", "minLength": 1 },
        "strategy": { "type": "string", "enum": ["PrimaryOnly", "PrimaryThenFallback", "FanOutCompare"] },
        "fallbackProviderIds": {
          "type": "array",
          "items": { "type": "string", "minLength": 1 }
        },
        "model": { "type": "string" },
        "maxTokens": { "type": "integer", "minimum": 1 },
        "temperature": { "type": "number" }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateGenerateOutputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["providerId", "model", "content"],
      "properties": {
        "providerId": { "type": "string" },
        "model": { "type": "string" },
        "content": { "type": "string" },
        "finishReason": { "type": "string" },
        "usage": {
          "type": "object",
          "properties": {
            "inputTokens": { "type": ["integer", "null"] },
            "outputTokens": { "type": ["integer", "null"] },
            "totalTokens": { "type": ["integer", "null"] }
          },
          "additionalProperties": false
        },
        "metadata": {
          "type": "object",
          "additionalProperties": { "type": "string" }
        }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateProvidersListInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "properties": {
        "probe": { "type": "boolean" },
        "probeTimeoutMilliseconds": { "type": "integer", "minimum": 1 }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateProvidersListOutputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["count", "providers"],
      "properties": {
        "count": { "type": "integer", "minimum": 0 },
        "probed": { "type": "boolean" },
        "providers": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["providerId", "displayName", "enabled", "supportsStreaming", "status"],
            "properties": {
              "providerId": { "type": "string" },
              "displayName": { "type": "string" },
              "enabled": { "type": "boolean" },
              "supportsStreaming": { "type": "boolean" },
              "status": { "type": "string", "enum": ["ready", "disabled", "notConfigured", "unauthorized", "unreachable", "timeout", "error"] },
              "probe": {
                "type": ["object", "null"],
                "properties": {
                  "status": { "type": "string", "enum": ["disabled", "notConfigured", "ready", "unauthorized", "unreachable", "timeout", "error"] },
                  "httpStatusCode": { "type": ["integer", "null"] },
                  "elapsedMilliseconds": { "type": ["integer", "null"] },
                  "message": { "type": ["string", "null"] },
                  "endpoint": { "type": ["string", "null"] }
                },
                "additionalProperties": false
              }
            },
            "additionalProperties": false
          }
        }
      },
      "additionalProperties": false
    }
    """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
