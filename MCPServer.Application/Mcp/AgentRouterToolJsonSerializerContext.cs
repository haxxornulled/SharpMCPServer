using System.Text.Json.Serialization;

namespace MCPServer.Application.Mcp;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(MCPServer.Application.Mcp.Tools.AgentRunCreateRequest))]
[JsonSerializable(typeof(MCPServer.Application.Mcp.Tools.AgentRunTargetRequest))]
[JsonSerializable(typeof(MCPServer.Application.Mcp.Tools.AgentRunApproveRequest))]
[JsonSerializable(typeof(MCPServer.Application.Mcp.Tools.AgentRunStructuredContent))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class AgentRouterToolJsonSerializerContext : JsonSerializerContext
{
}
