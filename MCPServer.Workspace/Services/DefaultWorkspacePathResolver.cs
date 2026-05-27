using LanguageExt;
using LanguageExt.Common;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Workspace.Services;

public sealed class DefaultWorkspacePathResolver : IWorkspacePathResolver
{
    private readonly IWorkspaceRootCatalog _rootCatalog;

    public DefaultWorkspacePathResolver(IWorkspaceRootCatalog rootCatalog)
    {
        _rootCatalog = rootCatalog ?? throw new ArgumentNullException(nameof(rootCatalog));
    }

    public Fin<WorkspaceFileLocation> Resolve(string rootName, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Fin.Fail<WorkspaceFileLocation>(Error.New("Workspace relative path is required."));
        }

        return _rootCatalog.FindRoot(rootName).Bind(root =>
        {
            var rootPath = Path.GetFullPath(root.Path);
            var combined = Path.GetFullPath(Path.Combine(rootPath, relativePath.Trim()));
            if (!IsPathInsideRoot(rootPath, combined))
            {
                return Fin.Fail<WorkspaceFileLocation>(Error.New("The requested path escapes the configured workspace root."));
            }

            return Fin.Succ(new WorkspaceFileLocation
            {
                RootName = root.Name,
                RootPath = rootPath,
                RelativePath = NormalizeRelativePath(rootPath, combined),
                FullPath = combined
            });
        });
    }

    private static bool IsPathInsideRoot(string rootPath, string candidatePath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRoot = Path.TrimEndingDirectorySeparator(rootPath);
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(candidatePath);

        return normalizedCandidate.Equals(normalizedRoot, comparison) ||
            normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static string NormalizeRelativePath(string rootPath, string fullPath)
    {
        var relative = Path.GetRelativePath(rootPath, fullPath);
        return relative == "." ? string.Empty : relative;
    }
}
