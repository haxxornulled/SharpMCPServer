using System.Text.Json.Serialization;
using MCPServer.Host.Sidecar.Profiles;
using MCPServer.Host.Sidecar.Vault;

namespace MCPServer.Host.Sidecar.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SshVaultDocument))]
[JsonSerializable(typeof(SshVaultKeyDocument))]
[JsonSerializable(typeof(SshProfilesDocument))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class HostSidecarJsonSerializerContext : JsonSerializerContext;
