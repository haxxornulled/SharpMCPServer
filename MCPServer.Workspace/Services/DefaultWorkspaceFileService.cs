using System.Text;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Workspace.Configuration;
using MCPServer.Workspace.Interfaces;
using MCPServer.Workspace.Models;

namespace MCPServer.Workspace.Services;

public sealed class DefaultWorkspaceFileService : IWorkspaceFileService
{
    private readonly McpWorkspaceOptions _options;
    private readonly IWorkspaceRootCatalog _rootCatalog;
    private readonly IWorkspacePathResolver _pathResolver;
    private readonly IWorkspacePatchApplier _patchApplier;

    public DefaultWorkspaceFileService(
        McpWorkspaceOptions options,
        IWorkspaceRootCatalog rootCatalog,
        IWorkspacePathResolver pathResolver,
        IWorkspacePatchApplier patchApplier)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rootCatalog = rootCatalog ?? throw new ArgumentNullException(nameof(rootCatalog));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _patchApplier = patchApplier ?? throw new ArgumentNullException(nameof(patchApplier));
    }

    public async ValueTask<Fin<WorkspaceFileReadResult>> ReadFileAsync(string rootName, string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var locationResult = _pathResolver.Resolve(rootName, relativePath);
        if (locationResult.IsFail)
        {
            return MapFailure<WorkspaceFileLocation, WorkspaceFileReadResult>(locationResult);
        }

        var location = Unwrap(locationResult, "Unexpected workspace path resolution failure.");
        var fileInfo = new FileInfo(location.FullPath);
        if (!fileInfo.Exists)
        {
            return Fin.Fail<WorkspaceFileReadResult>(Error.New($"Workspace file '{location.RelativePath}' was not found in root '{location.RootName}'."));
        }

        if (fileInfo.Length > _options.MaxReadBytes)
        {
            return Fin.Fail<WorkspaceFileReadResult>(Error.New($"Workspace file '{location.RelativePath}' exceeds the configured read limit."));
        }

        var content = await File.ReadAllTextAsync(location.FullPath, cancellationToken).ConfigureAwait(false);
        return Fin.Succ(new WorkspaceFileReadResult
        {
            RootName = location.RootName,
            Path = location.FullPath,
            RelativePath = location.RelativePath,
            Content = content,
            BytesRead = Encoding.UTF8.GetByteCount(content),
            LineCount = CountLines(content)
        });
    }

    public async ValueTask<Fin<WorkspaceFileSearchResult>> SearchAsync(string? rootName, string query, bool caseSensitive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
        {
            return Fin.Fail<WorkspaceFileSearchResult>(Error.New("Workspace search query is required."));
        }

        IReadOnlyList<WorkspaceRoot> roots;
        if (string.IsNullOrWhiteSpace(rootName))
        {
            roots = _rootCatalog.GetRoots();
        }
        else
        {
            var rootResult = _rootCatalog.FindRoot(rootName);
            if (rootResult.IsFail)
            {
                return MapFailure<WorkspaceRoot, WorkspaceFileSearchResult>(rootResult);
            }

            roots = [Unwrap(rootResult, "Unexpected workspace root lookup failure.")];
        }

        var hits = new List<WorkspaceFileSearchHit>();
        var filesScanned = 0;
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!root.Exists)
            {
                return Fin.Fail<WorkspaceFileSearchResult>(Error.New($"Workspace root '{root.Name}' does not exist."));
            }

            foreach (var filePath in EnumerateSearchableFiles(root.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (filesScanned >= _options.MaxSearchFiles)
                {
                    return Fin.Succ(new WorkspaceFileSearchResult
                    {
                        RootNames = roots.Select(item => item.Name).ToList(),
                        Query = query,
                        CaseSensitive = caseSensitive,
                        FilesScanned = filesScanned,
                        HitCount = hits.Count,
                        Truncated = true,
                        Hits = hits
                    });
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > _options.MaxReadBytes || LooksBinary(filePath))
                {
                    continue;
                }

                filesScanned++;
                var lineNumber = 0;
                foreach (var line in File.ReadLines(filePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lineNumber++;

                    var searchIndex = line.IndexOf(query, comparison);
                    if (searchIndex < 0)
                    {
                        continue;
                    }

                    hits.Add(new WorkspaceFileSearchHit
                    {
                        RootName = root.Name,
                        Path = filePath,
                        LineNumber = lineNumber,
                        MatchStart = searchIndex,
                        MatchLength = query.Length,
                        Line = line
                    });

                    if (hits.Count >= _options.MaxSearchResults)
                    {
                        return Fin.Succ(new WorkspaceFileSearchResult
                        {
                            RootNames = roots.Select(item => item.Name).ToList(),
                            Query = query,
                            CaseSensitive = caseSensitive,
                            FilesScanned = filesScanned,
                            HitCount = hits.Count,
                            Truncated = true,
                            Hits = hits
                        });
                    }
                }
            }
        }

        return Fin.Succ(new WorkspaceFileSearchResult
        {
            RootNames = roots.Select(item => item.Name).ToList(),
            Query = query,
            CaseSensitive = caseSensitive,
            FilesScanned = filesScanned,
            HitCount = hits.Count,
            Truncated = false,
            Hits = hits
        });
    }

    public async ValueTask<Fin<WorkspaceFileWriteResult>> WriteFileAsync(string rootName, string relativePath, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rootResult = ResolveWritableRoot(rootName);
        if (rootResult.IsFail)
        {
            return MapFailure<WorkspaceRoot, WorkspaceFileWriteResult>(rootResult);
        }

        var root = Unwrap(rootResult, "Unexpected workspace writable root lookup failure.");
        var locationResult = _pathResolver.Resolve(root.Name, relativePath);
        if (locationResult.IsFail)
        {
            return MapFailure<WorkspaceFileLocation, WorkspaceFileWriteResult>(locationResult);
        }

        var location = Unwrap(locationResult, "Unexpected workspace path resolution failure.");
        Directory.CreateDirectory(Path.GetDirectoryName(location.FullPath) ?? root.Path);
        await File.WriteAllTextAsync(location.FullPath, content, cancellationToken).ConfigureAwait(false);

        return Fin.Succ(new WorkspaceFileWriteResult
        {
            RootName = root.Name,
            Path = location.FullPath,
            RelativePath = location.RelativePath,
            BytesWritten = Encoding.UTF8.GetByteCount(content),
            LineCount = CountLines(content)
        });
    }

    public async ValueTask<Fin<WorkspacePatchResult>> ApplyPatchAsync(string rootName, string relativePath, string patch, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(message))
        {
            return Fin.Fail<WorkspacePatchResult>(Error.New("Workspace patch message is required."));
        }

        var rootResult = ResolveWritableRoot(rootName);
        if (rootResult.IsFail)
        {
            return MapFailure<WorkspaceRoot, WorkspacePatchResult>(rootResult);
        }

        var root = Unwrap(rootResult, "Unexpected workspace writable root lookup failure.");
        var locationResult = _pathResolver.Resolve(root.Name, relativePath);
        if (locationResult.IsFail)
        {
            return MapFailure<WorkspaceFileLocation, WorkspacePatchResult>(locationResult);
        }

        var location = Unwrap(locationResult, "Unexpected workspace path resolution failure.");
        var fileInfo = new FileInfo(location.FullPath);
        if (!fileInfo.Exists)
        {
            return Fin.Fail<WorkspacePatchResult>(Error.New($"Workspace file '{location.RelativePath}' was not found in root '{root.Name}'."));
        }

        if (fileInfo.Length > _options.MaxPatchBytes)
        {
            return Fin.Fail<WorkspacePatchResult>(Error.New($"Workspace file '{location.RelativePath}' exceeds the configured patch limit."));
        }

        var original = await File.ReadAllTextAsync(location.FullPath, cancellationToken).ConfigureAwait(false);
        var applied = _patchApplier.Apply(original, patch);
        if (applied.IsFail)
        {
            return MapFailure<WorkspacePatchApplicationResult, WorkspacePatchResult>(applied);
        }

        var patched = Unwrap(applied, "Unexpected workspace patch application failure.");
        await File.WriteAllTextAsync(location.FullPath, patched.Content, cancellationToken).ConfigureAwait(false);

        return Fin.Succ(new WorkspacePatchResult
        {
            RootName = root.Name,
            Path = location.FullPath,
            RelativePath = location.RelativePath,
            AppliedHunks = patched.AppliedHunks,
            AddedLines = patched.AddedLines,
            RemovedLines = patched.RemovedLines,
            BytesWritten = Encoding.UTF8.GetByteCount(patched.Content),
            Message = message.Trim()
        });
    }

    private Fin<WorkspaceRoot> ResolveWritableRoot(string rootName)
    {
        var rootResult = _rootCatalog.FindRoot(rootName);
        if (rootResult.IsFail)
        {
            return rootResult;
        }

        var root = Unwrap(rootResult, "Unexpected workspace root lookup failure.");
        if (!root.AllowWrite)
        {
            return Fin.Fail<WorkspaceRoot>(Error.New($"Workspace root '{root.Name}' is read-only."));
        }

        if (string.IsNullOrWhiteSpace(_options.ApprovalToken))
        {
            return Fin.Fail<WorkspaceRoot>(Error.New("Workspace writes are disabled until an approval token is configured."));
        }

        return rootResult;
    }

    private static TValue Unwrap<TValue>(Fin<TValue> result, string context)
    {
        return result.Match(
            Succ: static value => value,
            Fail: error => throw new InvalidOperationException($"{context} {error.Message}"));
    }

    private static Fin<TDestination> MapFailure<TSource, TDestination>(Fin<TSource> result)
    {
        return Fin.Fail<TDestination>(result.Match(
            Succ: static _ => throw new InvalidOperationException("Unexpected success while mapping a failure."),
            Fail: static error => error));
    }

    private IEnumerable<string> EnumerateSearchableFiles(string rootPath)
    {
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                var directoryName = Path.GetFileName(directory);
                if (ShouldSkipDirectory(directoryName))
                {
                    continue;
                }

                stack.Push(directory);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private bool ShouldSkipDirectory(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return false;
        }

        return _options.ExcludedDirectoryNames.Any(entry => string.Equals(entry, directoryName, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    }

    private static bool LooksBinary(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> buffer = stackalloc byte[4096];
            var bytesRead = stream.Read(buffer);
            for (var i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    return true;
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 0;
        }

        var count = 1;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }
}
