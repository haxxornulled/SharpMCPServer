using LanguageExt;
using LanguageExt.Common;
using MCPServer.Workspace.Configuration;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Workspace.Services;

public sealed class DefaultWorkspaceRootCatalog : IWorkspaceRootCatalog
{
    private readonly WorkspaceRoot[] _configuredRoots;
    private readonly IWorkspaceSandboxCatalog _sandboxCatalog;

    public DefaultWorkspaceRootCatalog(McpWorkspaceOptions options, IWorkspaceSandboxCatalog sandboxCatalog)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sandboxCatalog);

        _configuredRoots = BuildRoots(options);
        _sandboxCatalog = sandboxCatalog;
    }

    public IReadOnlyList<WorkspaceRoot> GetRoots()
    {
        var sandboxes = _sandboxCatalog.GetSandboxes();
        if (sandboxes.Count == 0)
        {
            return _configuredRoots;
        }

        var roots = new WorkspaceRoot[_configuredRoots.Length + sandboxes.Count];
        Array.Copy(_configuredRoots, roots, _configuredRoots.Length);
        for (var i = 0; i < sandboxes.Count; i++)
        {
            roots[_configuredRoots.Length + i] = sandboxes[i];
        }

        return roots;
    }

    public Fin<WorkspaceRoot> FindRoot(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fin.Fail<WorkspaceRoot>(Error.New("Workspace root name is required."));
        }

        var normalized = name.Trim();
        foreach (var root in _configuredRoots)
        {
            if (string.Equals(root.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return Fin.Succ(root);
            }
        }

        var sandboxes = _sandboxCatalog.GetSandboxes();
        foreach (var sandbox in sandboxes)
        {
            if (string.Equals(sandbox.Name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return Fin.Succ(sandbox);
            }
        }

        return Fin.Fail<WorkspaceRoot>(Error.New($"Workspace root '{name}' was not found."));
    }

    private static WorkspaceRoot[] BuildRoots(McpWorkspaceOptions options)
    {
        var configuredRoots = options.Roots.Count == 0
            ? [new McpWorkspaceRootOptions { Name = "workspace", Path = WorkspaceRootPathResolver.ResolveDefaultWorkspaceRootPath(), AllowWrite = true }]
            : options.Roots;

        var roots = new WorkspaceRoot[configuredRoots.Count];
        var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < configuredRoots.Count; i++)
        {
            var configured = configuredRoots[i];
            if (string.IsNullOrWhiteSpace(configured.Name))
            {
                throw new InvalidOperationException("Workspace root name cannot be empty.");
            }

            if (!names.Add(configured.Name.Trim()))
            {
                throw new InvalidOperationException($"Duplicate workspace root '{configured.Name.Trim()}' was configured.");
            }

            if (string.IsNullOrWhiteSpace(configured.Path))
            {
                throw new InvalidOperationException($"Workspace root '{configured.Name.Trim()}' must provide a path.");
            }

            var path = Path.GetFullPath(configured.Path.Trim(), AppContext.BaseDirectory);
            roots[i] = new WorkspaceRoot
            {
                Name = configured.Name.Trim(),
                Path = path,
                Kind = "workspace",
                AllowWrite = configured.AllowWrite,
                Exists = Directory.Exists(path)
            };
        }

        return roots;
    }
}
