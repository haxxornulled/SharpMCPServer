using System.Text.Json.Serialization;
using System.Text.Json;

namespace MCPServer.Domain.Mcp;

public sealed class InitializeRequest
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; init; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public JsonElement Capabilities { get; init; }

    [JsonPropertyName("clientInfo")]
    public McpImplementationInfo ClientInfo { get; init; } = new McpImplementationInfo();
}

public sealed class InitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; init; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public McpServerCapabilities Capabilities { get; init; } = new McpServerCapabilities();

    [JsonPropertyName("serverInfo")]
    public McpImplementationInfo ServerInfo { get; init; } = new McpImplementationInfo();

    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; init; }
}
