namespace MCPServer.Workspace.Models;

public sealed class WorkspaceRoot
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Kind { get; set; } = "workspace";

    public bool AllowWrite { get; set; }

    public bool Exists { get; set; }

    public string? SourceRootName { get; set; }

    public DateTimeOffset? CreatedUtc { get; set; }
}

public sealed class WorkspaceFileLocation
{
    public string RootName { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;
}

public sealed class WorkspaceRootsListResult
{
    public List<WorkspaceRoot> Roots { get; set; } = [];
}

public sealed class WorkspaceSandboxesListResult
{
    public List<WorkspaceRoot> Sandboxes { get; set; } = [];
}

public sealed class WorkspaceSandboxCreateResult
{
    public WorkspaceRoot Sandbox { get; set; } = new();
}

public sealed class WorkspaceSandboxDeleteResult
{
    public string SandboxName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public bool Deleted { get; set; }

    public string? SourceRootName { get; set; }

    public DateTimeOffset? CreatedUtc { get; set; }
}

public sealed class WorkspaceFileReadRequest
{
    public string RootName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;
}

public sealed class WorkspaceFileReadResult
{
    public string RootName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string Encoding { get; set; } = "utf-8";

    public long BytesRead { get; set; }

    public int LineCount { get; set; }

    public string Content { get; set; } = string.Empty;
}

public sealed class WorkspaceFileSearchHit
{
    public string RootName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public int LineNumber { get; set; }

    public int MatchStart { get; set; }

    public int MatchLength { get; set; }

    public string Line { get; set; } = string.Empty;
}

public sealed class WorkspaceFileSearchRequest
{
    public string? RootName { get; set; }

    public string Query { get; set; } = string.Empty;

    public bool CaseSensitive { get; set; }
}

public sealed class WorkspaceSandboxCreateRequest
{
    public string SourceRootName { get; set; } = string.Empty;

    public string? SandboxName { get; set; }

    public string ApprovalToken { get; set; } = string.Empty;
}

public sealed class WorkspaceSandboxDeleteRequest
{
    public string SandboxName { get; set; } = string.Empty;

    public string ApprovalToken { get; set; } = string.Empty;
}

public sealed class WorkspaceFileSearchResult
{
    public List<string> RootNames { get; set; } = [];

    public string Query { get; set; } = string.Empty;

    public bool CaseSensitive { get; set; }

    public int FilesScanned { get; set; }

    public int HitCount { get; set; }

    public bool Truncated { get; set; }

    public List<WorkspaceFileSearchHit> Hits { get; set; } = [];
}

public sealed class WorkspaceFileWriteRequest
{
    public string RootName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}

public sealed class WorkspaceFileWriteResult
{
    public string RootName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public long BytesWritten { get; set; }

    public int LineCount { get; set; }
}

public sealed class WorkspacePatchRequest
{
    public string RootName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string Patch { get; set; } = string.Empty;
}

public sealed class WorkspacePatchResult
{
    public string RootName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public int AppliedHunks { get; set; }

    public int AddedLines { get; set; }

    public int RemovedLines { get; set; }

    public long BytesWritten { get; set; }
}

public sealed class WorkspacePatchApplicationResult
{
    public string Content { get; set; } = string.Empty;

    public int AppliedHunks { get; set; }

    public int AddedLines { get; set; }

    public int RemovedLines { get; set; }
}
