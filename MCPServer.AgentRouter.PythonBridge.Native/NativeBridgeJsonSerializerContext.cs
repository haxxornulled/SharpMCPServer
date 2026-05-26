using System.Text.Json.Serialization;
using MCPServer.AgentRouter.Abstractions.Models;

namespace MCPServer.AgentRouter.PythonBridge.Native;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(Dictionary<string, string?>))]
[JsonSerializable(typeof(AgentRouterBridgeRequest))]
[JsonSerializable(typeof(AgentRouterBridgeResponse))]
internal partial class NativeBridgeJsonSerializerContext : JsonSerializerContext
{
}
