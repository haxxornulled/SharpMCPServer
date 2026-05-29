using System.Text.Json;

namespace MCPServer.Application.Mcp.Tools;

internal static class AgentToolSchemas
{
    public static JsonElement CreateCreateInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["objective", "capability"],
      "properties": {
        "objective": { "type": "string", "minLength": 1 },
        "capability": { "type": "string", "minLength": 1 },
        "workflowMode": { "type": "string", "enum": ["deterministic", "agentic"] },
        "routeTarget": { "type": "string", "enum": ["local", "local-model", "remote", "remote-api", "mcp", "mcp-server"] }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateSubagentCreateInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["objective", "capability", "parentRunId"],
      "properties": {
        "objective": { "type": "string", "minLength": 1 },
        "capability": { "type": "string", "minLength": 1 },
        "parentRunId": { "type": "string", "minLength": 1 },
        "workflowMode": { "type": "string", "enum": ["deterministic", "agentic"] },
        "routeTarget": { "type": "string", "enum": ["local", "local-model", "remote", "remote-api", "mcp", "mcp-server"] }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateTargetInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["runId"],
      "properties": {
        "runId": { "type": "string", "minLength": 1 }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateApproveInputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["runId", "approvalId"],
      "properties": {
        "runId": { "type": "string", "minLength": 1 },
        "approvalId": { "type": "string", "minLength": 1 },
        "approvedBy": { "type": "string" }
      },
      "additionalProperties": false
    }
    """);

    public static JsonElement CreateOutputSchema() => Parse("""
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": [
        "runId",
        "objective",
        "status",
        "message",
        "createdAtUtc",
        "updatedAtUtc",
        "version",
        "capability",
        "workflowMode",
        "routeTarget",
        "kind",
        "parentRunId",
        "approvalGranted",
        "approvalId",
        "approvedBy",
        "metadata"
      ],
      "properties": {
        "runId": { "type": "string" },
        "objective": { "type": "string" },
        "status": { "type": "string" },
        "message": { "type": "string" },
        "createdAtUtc": { "type": "string", "format": "date-time" },
        "updatedAtUtc": { "type": "string", "format": "date-time" },
        "version": { "type": "integer" },
        "capability": { "type": "string" },
        "workflowMode": { "type": "string" },
        "routeTarget": { "type": "string" },
        "kind": { "type": "string" },
        "parentRunId": { "type": "string" },
        "approvalGranted": { "type": "boolean" },
        "approvalId": { "type": "string" },
        "approvedBy": { "type": "string" },
        "metadata": {
          "type": "object",
          "additionalProperties": {
            "type": "string"
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
