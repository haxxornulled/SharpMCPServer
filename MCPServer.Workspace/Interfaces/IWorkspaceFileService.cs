using LanguageExt;
using MCPServer.Workspace.Models;

namespace MCPServer.Workspace.Interfaces;

public interface IWorkspaceFileService
{
    ValueTask<Fin<WorkspaceFileReadResult>> ReadFileAsync(string rootName, string relativePath, CancellationToken cancellationToken);

    ValueTask<Fin<WorkspaceFileSearchResult>> SearchAsync(string? rootName, string query, bool caseSensitive, CancellationToken cancellationToken);

    ValueTask<Fin<WorkspaceFileWriteResult>> WriteFileAsync(string rootName, string relativePath, string content, CancellationToken cancellationToken);

    ValueTask<Fin<WorkspacePatchResult>> ApplyPatchAsync(string rootName, string relativePath, string patch, string message, CancellationToken cancellationToken);
}
