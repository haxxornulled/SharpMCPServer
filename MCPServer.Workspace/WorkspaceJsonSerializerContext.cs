using System.Text.Json.Serialization;
using MCPServer.Workspace.Models;

namespace MCPServer.Workspace;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(WorkspaceRoot))]
[JsonSerializable(typeof(WorkspaceRoot[]))]
[JsonSerializable(typeof(WorkspaceRootsListResult))]
[JsonSerializable(typeof(WorkspaceSandboxesListResult))]
[JsonSerializable(typeof(WorkspaceSandboxCreateResult))]
[JsonSerializable(typeof(WorkspaceSandboxDeleteResult))]
[JsonSerializable(typeof(WorkspaceFileReadRequest))]
[JsonSerializable(typeof(WorkspaceFileLocation))]
[JsonSerializable(typeof(WorkspaceFileReadResult))]
[JsonSerializable(typeof(WorkspaceFileSearchHit))]
[JsonSerializable(typeof(WorkspaceFileSearchHit[]))]
[JsonSerializable(typeof(WorkspaceFileSearchRequest))]
[JsonSerializable(typeof(WorkspaceFileSearchResult))]
[JsonSerializable(typeof(WorkspaceSandboxCreateRequest))]
[JsonSerializable(typeof(WorkspaceSandboxDeleteRequest))]
[JsonSerializable(typeof(WorkspaceFileWriteRequest))]
[JsonSerializable(typeof(WorkspaceFileWriteResult))]
[JsonSerializable(typeof(WorkspacePatchRequest))]
[JsonSerializable(typeof(WorkspacePatchResult))]
[JsonSerializable(typeof(WorkspacePatchApplicationResult))]
public sealed partial class WorkspaceJsonSerializerContext : JsonSerializerContext
{
}
