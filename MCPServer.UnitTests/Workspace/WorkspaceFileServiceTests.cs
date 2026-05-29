using LanguageExt;
using MCPServer.UnitTests.Mcp;
using MCPServer.Workspace.Configuration;
using MCPServer.Workspace.Services;
using MCPServer.Workspace.Stores;
using Xunit;

namespace MCPServer.UnitTests.Workspace;

public sealed class WorkspaceFileServiceTests : IDisposable
{
    private readonly string _rootPath;
    private readonly string _databasePath;
    private readonly DefaultWorkspaceFileService _service;

    public WorkspaceFileServiceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "MCPServer.Workspace.Tests", Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(Path.GetTempPath(), "MCPServer.Workspace.Tests", "workspace-" + Guid.NewGuid().ToString("N"), "workspace.db");
        Directory.CreateDirectory(Path.Combine(_rootPath, "src"));
        File.WriteAllText(Path.Combine(_rootPath, "src", "Program.cs"), "using System;\nConsole.WriteLine(\"Hello\");\n");

        var options = CreateOptions(_rootPath, "approved", _databasePath);
        var sandboxRegistry = new SqliteWorkspaceSandboxRegistry(options);
        var sandboxManager = new DefaultWorkspaceSandboxManager(options, sandboxRegistry);
        var rootCatalog = new DefaultWorkspaceRootCatalog(options, sandboxManager);
        var pathResolver = new DefaultWorkspacePathResolver(rootCatalog);
        var patchApplier = new WorkspacePatchApplier();
        _service = new DefaultWorkspaceFileService(options, rootCatalog, pathResolver, patchApplier);
    }

    [Fact]
    public async Task ReadFileAsync_Returns_File_Content_From_Configured_Root()
    {
        var result = await _service.ReadFileAsync("workspace", "src/Program.cs", CancellationToken.None);
        var read = TestFin.Success(result);

        Assert.Equal("workspace", read.RootName);
        Assert.EndsWith(Path.Combine("src", "Program.cs"), read.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("src/Program.cs".Replace('/', Path.DirectorySeparatorChar), read.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Contains("Console.WriteLine(\"Hello\");", read.Content);
        Assert.Equal(3, read.LineCount);
    }

    [Fact]
    public async Task SearchAsync_Returns_Hits_Without_Throwing_For_Missing_Root()
    {
        var result = await _service.SearchAsync("missing", "Hello", caseSensitive: false, CancellationToken.None);

        var error = TestFin.Failure(result);
        Assert.Contains("was not found", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_Finds_Text_Within_Configured_Root()
    {
        var result = await _service.SearchAsync("workspace", "Console.WriteLine", caseSensitive: false, CancellationToken.None);
        var search = TestFin.Success(result);

        Assert.Equal(1, search.HitCount);
        Assert.False(search.Truncated);
        Assert.Equal("workspace", search.RootNames.Single());
        Assert.Single(search.Hits);
        Assert.Equal(2, search.Hits[0].LineNumber);
    }

    [Fact]
    public async Task WriteFileAsync_Rejects_Writes_When_No_Approval_Token_Is_Configured()
    {
        var blockedDatabasePath = Path.Combine(Path.GetTempPath(), "MCPServer.Workspace.Tests", "blocked-" + Guid.NewGuid().ToString("N"), "workspace.db");
        var blockedOptions = CreateOptions(_rootPath, string.Empty, blockedDatabasePath);
        var blockedSandboxRegistry = new SqliteWorkspaceSandboxRegistry(blockedOptions);
        var blockedSandboxManager = new DefaultWorkspaceSandboxManager(blockedOptions, blockedSandboxRegistry);
        var blockedRootCatalog = new DefaultWorkspaceRootCatalog(blockedOptions, blockedSandboxManager);
        var blockedPathResolver = new DefaultWorkspacePathResolver(blockedRootCatalog);
        var blockedPatchApplier = new WorkspacePatchApplier();
        var blockedService = new DefaultWorkspaceFileService(blockedOptions, blockedRootCatalog, blockedPathResolver, blockedPatchApplier);

        var result = await blockedService.WriteFileAsync("workspace", "src/Blocked.cs", "blocked", CancellationToken.None);

        var error = TestFin.Failure(result);
        Assert.Contains("approval token", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteFileAsync_Writes_Text_Within_Configured_Root()
    {
        var result = await _service.WriteFileAsync("workspace", "src/Written.cs", "namespace Demo;\n", CancellationToken.None);
        var write = TestFin.Success(result);

        Assert.Equal("workspace", write.RootName);
        Assert.EndsWith(Path.Combine("src", "Written.cs"), write.Path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(_rootPath, "src", "Written.cs")));
    }

    [Fact]
    public async Task ApplyPatchAsync_Transforms_Text_And_Writes_Back()
    {
        var patch = """
        diff --git a/src/Program.cs b/src/Program.cs
        --- a/src/Program.cs
        +++ b/src/Program.cs
        @@ -1,2 +1,2 @@
        -using System;
        +using System;
        -Console.WriteLine("Hello");
        +Console.WriteLine("Hello, workspace!");
        """;

        var result = await _service.ApplyPatchAsync("workspace", "src/Program.cs", patch, "Fix the hello world output.", CancellationToken.None);
        var applied = TestFin.Success(result);

        Assert.Equal(1, applied.AppliedHunks);
        Assert.True(applied.BytesWritten > 0);
        Assert.Equal("Fix the hello world output.", applied.Message);

        var patchedText = await File.ReadAllTextAsync(Path.Combine(_rootPath, "src", "Program.cs"), CancellationToken.None);
        Assert.Contains("Hello, workspace!", patchedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyPatchAsync_Rejects_Blank_Message()
    {
        var patch = """
        diff --git a/src/Program.cs b/src/Program.cs
        --- a/src/Program.cs
        +++ b/src/Program.cs
        @@ -1,2 +1,2 @@
        -using System;
        +using System;
        -Console.WriteLine("Hello");
        +Console.WriteLine("Hello, workspace!");
        """;

        var result = await _service.ApplyPatchAsync("workspace", "src/Program.cs", patch, " ", CancellationToken.None);

        var error = TestFin.Failure(result);
        Assert.Contains("patch message is required", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadFileAsync_Rejects_Path_Escapes()
    {
        var result = await _service.ReadFileAsync("workspace", "..\\escape.txt", CancellationToken.None);

        var error = TestFin.Failure(result);
        Assert.Contains("escapes the configured workspace root", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }

        var databaseDirectory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory) && Directory.Exists(databaseDirectory))
        {
            Directory.Delete(databaseDirectory, recursive: true);
        }
    }

    private static McpWorkspaceOptions CreateOptions(string rootPath, string approvalToken)
        => CreateOptions(rootPath, approvalToken, Path.Combine(Path.GetTempPath(), "MCPServer.Workspace.Tests", Guid.NewGuid().ToString("N"), "workspace.db"));

    private static McpWorkspaceOptions CreateOptions(string rootPath, string approvalToken, string databasePath)
    {
        return new McpWorkspaceOptions
        {
            ApprovalToken = approvalToken,
            Sqlite =
            {
                DatabasePath = databasePath,
                EnsureCreatedOnUse = true
            },
            Roots =
            [
                new McpWorkspaceRootOptions
                {
                    Name = "workspace",
                    Path = rootPath,
                    AllowWrite = true
                }
            ]
        };
    }
}
