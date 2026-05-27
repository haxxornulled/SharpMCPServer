using LanguageExt;
using MCPServer.Workspace.Models;

namespace MCPServer.Workspace.Interfaces;

public interface IWorkspacePatchApplier
{
    Fin<WorkspacePatchApplicationResult> Apply(string originalText, string patchText);
}
