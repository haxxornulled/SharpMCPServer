using System.Text.Json;

namespace MCPServer.Tools.Workspace;

internal static class WorkspaceToolSchemas
{
    public static JsonElement CreateRootsListInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateRootsListOutputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["roots"],
      "properties": {
        "roots": {
          "type": "array",
          "items": {
          "type": "object",
            "required": ["name", "path", "kind", "allowWrite", "exists"],
            "properties": {
              "name": { "type": "string" },
              "path": { "type": "string" },
              "kind": { "type": "string", "enum": ["workspace", "sandbox"] },
              "allowWrite": { "type": "boolean" },
              "exists": { "type": "boolean" },
              "sourceRootName": { "type": "string" },
              "createdUtc": { "type": "string", "format": "date-time" }
            },
            "additionalProperties": false
          }
        }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateSandboxesListOutputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["sandboxes"],
      "properties": {
        "sandboxes": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["name", "path", "kind", "allowWrite", "exists"],
            "properties": {
              "name": { "type": "string" },
              "path": { "type": "string" },
              "kind": { "type": "string", "enum": ["sandbox"] },
              "allowWrite": { "type": "boolean" },
              "exists": { "type": "boolean" },
              "sourceRootName": { "type": "string" },
              "createdUtc": { "type": "string", "format": "date-time" }
            },
            "additionalProperties": false
          }
        }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateFileReadInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["rootName", "relativePath"],
      "properties": {
        "rootName": { "type": "string" },
        "relativePath": { "type": "string" }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateFileReadOutputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["rootName", "path", "relativePath", "encoding", "bytesRead", "lineCount", "content"],
      "properties": {
        "rootName": { "type": "string" },
        "path": { "type": "string" },
        "relativePath": { "type": "string" },
        "encoding": { "type": "string" },
        "bytesRead": { "type": "integer" },
        "lineCount": { "type": "integer" },
        "content": { "type": "string" }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateFileSearchInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["query"],
      "properties": {
        "rootName": { "type": "string" },
        "query": { "type": "string" },
        "caseSensitive": { "type": "boolean" }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateFileSearchOutputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["rootNames", "query", "caseSensitive", "filesScanned", "hitCount", "truncated", "hits"],
      "properties": {
        "rootNames": {
          "type": "array",
          "items": { "type": "string" }
        },
        "query": { "type": "string" },
        "caseSensitive": { "type": "boolean" },
        "filesScanned": { "type": "integer" },
        "hitCount": { "type": "integer" },
        "truncated": { "type": "boolean" },
        "hits": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["rootName", "path", "lineNumber", "matchStart", "matchLength", "line"],
            "properties": {
              "rootName": { "type": "string" },
              "path": { "type": "string" },
              "lineNumber": { "type": "integer" },
              "matchStart": { "type": "integer" },
              "matchLength": { "type": "integer" },
              "line": { "type": "string" }
            },
            "additionalProperties": false
          }
        }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateFileWriteInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["rootName", "relativePath", "content"],
      "properties": {
        "rootName": { "type": "string" },
        "relativePath": { "type": "string" },
        "content": { "type": "string" }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateFileWriteOutputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["rootName", "path", "relativePath", "bytesWritten", "lineCount"],
      "properties": {
        "rootName": { "type": "string" },
        "path": { "type": "string" },
        "relativePath": { "type": "string" },
        "bytesWritten": { "type": "integer" },
        "lineCount": { "type": "integer" }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateFilePatchInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["rootName", "relativePath", "patch", "message"],
      "properties": {
        "rootName": { "type": "string" },
        "relativePath": { "type": "string" },
        "patch": { "type": "string" },
        "message": { "type": "string" }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateFilePatchOutputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["rootName", "path", "relativePath", "appliedHunks", "addedLines", "removedLines", "bytesWritten", "message"],
      "properties": {
        "rootName": { "type": "string" },
        "path": { "type": "string" },
        "relativePath": { "type": "string" },
        "appliedHunks": { "type": "integer" },
        "addedLines": { "type": "integer" },
        "removedLines": { "type": "integer" },
        "bytesWritten": { "type": "integer" },
        "message": { "type": "string" }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateSandboxCreateInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["sourceRootName"],
      "properties": {
        "sourceRootName": { "type": "string" },
        "sandboxName": { "type": "string" }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateSandboxCreateOutputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["sandbox"],
      "properties": {
        "sandbox": {
          "type": "object",
          "required": ["name", "path", "kind", "allowWrite", "exists"],
          "properties": {
            "name": { "type": "string" },
            "path": { "type": "string" },
            "kind": { "type": "string", "enum": ["sandbox"] },
            "allowWrite": { "type": "boolean" },
            "exists": { "type": "boolean" },
            "sourceRootName": { "type": "string" },
            "createdUtc": { "type": "string", "format": "date-time" }
          },
          "additionalProperties": false
        }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateSandboxDeleteInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["sandboxName"],
      "properties": {
        "sandboxName": { "type": "string" }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateSandboxDeleteOutputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["sandboxName", "path", "deleted"],
      "properties": {
        "sandboxName": { "type": "string" },
        "path": { "type": "string" },
        "deleted": { "type": "boolean" },
        "sourceRootName": { "type": "string" },
        "createdUtc": { "type": "string", "format": "date-time" }
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
