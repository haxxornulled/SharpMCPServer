using LanguageExt;
using MCPServer.Workspace.Models;

namespace MCPServer.Workspace.Interfaces;

public interface IWorkspaceSandboxCatalog
{
    IReadOnlyList<WorkspaceRoot> GetSandboxes();

    Fin<WorkspaceRoot> FindSandbox(string name);
}
