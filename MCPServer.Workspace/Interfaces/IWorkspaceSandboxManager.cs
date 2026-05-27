using LanguageExt;
using MCPServer.Workspace.Models;

namespace MCPServer.Workspace.Interfaces;

public interface IWorkspaceSandboxManager
{
    ValueTask<Fin<WorkspaceRoot>> CreateAsync(string sourceRootName, string? sandboxName, string approvalToken, CancellationToken cancellationToken);

    ValueTask<Fin<WorkspaceRoot>> DeleteAsync(string sandboxName, string approvalToken, CancellationToken cancellationToken);
}
