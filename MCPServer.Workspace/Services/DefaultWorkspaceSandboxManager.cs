using LanguageExt;
using LanguageExt.Common;
using MCPServer.Workspace.Configuration;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;
using MCPServer.Workspace.Stores;

namespace MCPServer.Workspace.Services;

public sealed class DefaultWorkspaceSandboxManager : IWorkspaceSandboxCatalog, IWorkspaceSandboxManager
{
    private readonly McpWorkspaceOptions _options;
    private readonly SqliteWorkspaceSandboxRegistry _registry;

    public DefaultWorkspaceSandboxManager(
        McpWorkspaceOptions options,
        SqliteWorkspaceSandboxRegistry registry)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IReadOnlyList<WorkspaceRoot> GetSandboxes()
    {
        return _registry.GetSandboxes();
    }

    public Fin<WorkspaceRoot> FindSandbox(string name)
    {
        return _registry.FindSandbox(name);
    }

    public async ValueTask<Fin<WorkspaceRoot>> CreateAsync(string sourceRootName, string? sandboxName, string approvalToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var approvalResult = ValidateApprovalToken(approvalToken);
        if (approvalResult.IsFail)
        {
            return Fin.Fail<WorkspaceRoot>(approvalResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected workspace approval validation success while handling failure."),
                Fail: static error => error));
        }

        var sourceResult = FindSourceRoot(sourceRootName);
        if (sourceResult.IsFail)
        {
            return Fin.Fail<WorkspaceRoot>(sourceResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected workspace source resolution success while handling failure."),
                Fail: static error => error));
        }

        var source = sourceResult.Match(
            Succ: static value => value,
            Fail: static error => throw new InvalidOperationException($"Unexpected workspace source resolution failure: {error.Message}"));

        if (!source.Exists)
        {
            return Fin.Fail<WorkspaceRoot>(Error.New($"Workspace source root '{source.Name}' does not exist."));
        }

        var normalizedName = NormalizeSandboxName(sandboxName) ?? GenerateSandboxName(source.Name);
        if (NameConflicts(normalizedName))
        {
            return Fin.Fail<WorkspaceRoot>(Error.New($"Workspace sandbox '{normalizedName}' is already in use."));
        }

        return await _registry.CreateAsync(source, normalizedName, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<Fin<WorkspaceRoot>> DeleteAsync(string sandboxName, string approvalToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var approvalResult = ValidateApprovalToken(approvalToken);
        if (approvalResult.IsFail)
        {
            return ValueTask.FromResult(Fin.Fail<WorkspaceRoot>(approvalResult.Match(
                Succ: static _ => throw new InvalidOperationException("Unexpected workspace approval validation success while handling failure."),
                Fail: static error => error)));
        }

        return _registry.DeleteAsync(sandboxName, cancellationToken);
    }

    private Fin<Unit> ValidateApprovalToken(string approvalToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApprovalToken))
        {
            return Fin.Fail<Unit>(Error.New("Workspace sandbox operations are disabled until an approval token is configured."));
        }

        return string.Equals(approvalToken?.Trim(), _options.ApprovalToken.Trim(), StringComparison.Ordinal)
            ? Fin.Succ(Unit.Default)
            : Fin.Fail<Unit>(Error.New("Workspace approval token was rejected."));
    }

    private Fin<WorkspaceRoot> FindSourceRoot(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fin.Fail<WorkspaceRoot>(Error.New("Workspace source root name is required."));
        }

        var normalized = name.Trim();
        if (_options.Roots.Count == 0 && string.Equals(normalized, "workspace", StringComparison.OrdinalIgnoreCase))
        {
            var path = WorkspaceRootPathResolver.ResolveDefaultWorkspaceRootPath();
            return Fin.Succ(new WorkspaceRoot
            {
                Name = "workspace",
                Path = path,
                Kind = "workspace",
                AllowWrite = true,
                Exists = Directory.Exists(path)
            });
        }

        foreach (var root in _options.Roots)
        {
            var rootName = root.Name?.Trim();
            if (string.IsNullOrWhiteSpace(rootName) || !string.Equals(rootName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = string.IsNullOrWhiteSpace(root.Path)
                ? string.Empty
                : Path.GetFullPath(root.Path.Trim(), AppContext.BaseDirectory);
            return Fin.Succ(new WorkspaceRoot
            {
                Name = rootName,
                Path = path,
                Kind = "workspace",
                AllowWrite = root.AllowWrite,
                Exists = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)
            });
        }

        var sandboxResult = _registry.FindSandbox(normalized);
        return sandboxResult.IsFail
            ? Fin.Fail<WorkspaceRoot>(Error.New($"Workspace source root '{name}' was not found."))
            : sandboxResult;
    }

    private bool NameConflicts(string name)
    {
        if (_options.Roots.Count == 0 && string.Equals(name, "workspace", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var root in _options.Roots)
        {
            var rootName = root.Name?.Trim();
            if (!string.IsNullOrWhiteSpace(rootName) && string.Equals(rootName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return _registry.GetSandboxes().Any(sandbox => string.Equals(sandbox.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeSandboxName(string? sandboxName)
    {
        if (string.IsNullOrWhiteSpace(sandboxName))
        {
            return null;
        }

        var trimmed = sandboxName.Trim();
        if (!IsSafeSegment(trimmed))
        {
            throw new InvalidOperationException("Workspace sandbox name must be a simple filesystem-safe name.");
        }

        return trimmed;
    }

    private static string GenerateSandboxName(string sourceName)
    {
        var prefix = string.IsNullOrWhiteSpace(sourceName) ? "sandbox" : sourceName.Trim();
        prefix = new string(prefix.Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "sandbox";
        }

        return $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}".ToLowerInvariant();
    }

    private static bool IsSafeSegment(string value)
    {
        if (value.Length == 0 || value is "." or "..")
        {
            return false;
        }

        var invalid = Path.GetInvalidFileNameChars();
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch is '/' or '\\' or ':' || invalid.Contains(ch))
            {
                return false;
            }
        }

        return true;
    }
}
