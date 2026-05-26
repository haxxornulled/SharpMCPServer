using System.Text.Json.Serialization;

namespace MCPServer.AgentRouter.Infrastructure.Persistence;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(Dictionary<string, string?>))]
internal sealed partial class AgentRouterInfrastructureJsonSerializerContext : JsonSerializerContext
{
}
