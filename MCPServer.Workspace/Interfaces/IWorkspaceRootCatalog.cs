using LanguageExt;
using MCPServer.Workspace.Models;

namespace MCPServer.Workspace.Interfaces;

public interface IWorkspaceRootCatalog
{
    IReadOnlyList<WorkspaceRoot> GetRoots();

    Fin<WorkspaceRoot> FindRoot(string name);
}
