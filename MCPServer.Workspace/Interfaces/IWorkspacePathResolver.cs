using LanguageExt;
using MCPServer.Workspace.Models;

namespace MCPServer.Workspace.Interfaces;

public interface IWorkspacePathResolver
{
    Fin<WorkspaceFileLocation> Resolve(string rootName, string relativePath);
}
