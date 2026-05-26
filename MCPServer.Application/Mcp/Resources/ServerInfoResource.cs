using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Resources;

public sealed class ServerInfoResource : IMcpResource
{
    private static readonly McpResourceDescriptor ServerInfoDescriptor = new McpResourceDescriptor
    {
        Uri = McpResourceUris.ServerInfo,
        Name = "server.info",
        Title = "Server Info",
        Description = "Static metadata about this MCP server implementation.",
        MimeType = "application/json"
    };

    public McpResourceDescriptor Descriptor => ServerInfoDescriptor;

    public ValueTask<Fin<ResourcesReadResult>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var structured = new ServerInfoStructuredContent
        {
            Name = "MCPServer",
            ProtocolVersion = McpProtocolVersions.Current,
            ImplementationProfile = "stdio-baseline-2025-11-25",
            Capabilities = ["tools", "resources", "logging", "tasks", "sampling/createMessage", "elicitation/create", "notifications/tasks/status"]
        };

        var json = JsonSerializer.Serialize(structured, McpJsonSerializerContext.Default.ServerInfoStructuredContent);
        var result = new ResourcesReadResult
        {
            Contents =
            [
                new ResourceContent
                {
                    Uri = McpResourceUris.ServerInfo,
                    MimeType = "application/json",
                    Text = json
                }
            ]
        };

        return new ValueTask<Fin<ResourcesReadResult>>(Fin.Succ<ResourcesReadResult>(result));
    }
}
