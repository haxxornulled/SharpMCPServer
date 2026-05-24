using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Tools;

public sealed class ServerInfoTool : IMcpTool
{
    private static readonly JsonElement NoParameterInputSchema = CreateNoParameterInputSchema();
    private static readonly JsonElement ServerInfoOutputSchema = CreateServerInfoOutputSchema();

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = McpToolNames.ServerInfo,
        Title = "Server Information",
        Description = "Returns basic information about this MCP server implementation.",
        InputSchema = NoParameterInputSchema,
        OutputSchema = ServerInfoOutputSchema,
        Execution = new McpToolExecution
        {
            TaskSupport = McpToolTaskSupport.Forbidden
        }
    };

    public ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments is { } suppliedArgumentElement && suppliedArgumentElement is not { ValueKind: JsonValueKind.Object })
        {
            return new ValueTask<Fin<ToolCallResult>>(Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{McpToolNames.ServerInfo} expects an object for arguments.", isError: true)));
        }

        if (arguments is { } suppliedArguments && suppliedArguments is { ValueKind: JsonValueKind.Object })
        {
            using var properties = suppliedArguments.EnumerateObject();
            if (properties.MoveNext())
            {
                return new ValueTask<Fin<ToolCallResult>>(Fin.Succ<ToolCallResult>(ToolCallResult.Text($"{McpToolNames.ServerInfo} does not accept arguments.", isError: true)));
            }
        }

        var structuredContent = new ServerInfoStructuredContent
        {
            Name = "MCPServer",
            ProtocolVersion = McpProtocolVersions.Current,
            Phase = "1",
            Capabilities = new[]
            {
                "initialize",
                "notifications/initialized",
                "ping",
                "logging/setLevel",
                "tools/list",
                "tools/call",
                "resources/list",
                "resources/read",
                "resources/templates/list",
                "prompts/list",
                "prompts/get"
            }
        };

        var structuredContentJson = JsonSerializer.SerializeToElement(
            structuredContent,
            McpJsonSerializerContext.Default.ServerInfoStructuredContent);

        return new ValueTask<Fin<ToolCallResult>>(Fin.Succ<ToolCallResult>(ToolCallResult.Text(
            structuredContentJson.GetRawText(),
            structuredContent: structuredContentJson)));
    }

    private static JsonElement CreateNoParameterInputSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false
        }
        """);

        return document.RootElement.Clone();
    }

    private static JsonElement CreateServerInfoOutputSchema()
    {
        using var document = JsonDocument.Parse("""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["name", "protocolVersion", "phase", "capabilities"],
          "properties": {
            "name": { "type": "string", "minLength": 1 },
            "protocolVersion": { "type": "string", "minLength": 1 },
            "phase": { "type": "string", "minLength": 1 },
            "capabilities": {
              "type": "array",
              "items": { "type": "string", "minLength": 1 }
            }
          },
          "additionalProperties": false
        }
        """);

        return document.RootElement.Clone();
    }
}
