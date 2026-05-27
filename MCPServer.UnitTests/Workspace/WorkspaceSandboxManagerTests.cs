using MCPServer.UnitTests.Mcp;
using MCPServer.Workspace.Configuration;
using MCPServer.Workspace.Services;
using Xunit;

namespace MCPServer.UnitTests.Workspace;

public sealed class WorkspaceSandboxManagerTests : IDisposable
{
    private readonly string _sourceRootPath;
    private readonly string _sandboxBasePath;
    private readonly string _databasePath;
    private readonly McpWorkspaceOptions _options;
    private readonly DefaultWorkspaceSandboxManager _sandboxManager;
    private readonly DefaultWorkspaceRootCatalog _rootCatalog;
    private readonly DefaultWorkspaceFileService _fileService;

    public WorkspaceSandboxManagerTests()
    {
        _sourceRootPath = Path.Combine(Path.GetTempPath(), "MCPServer.Workspace.Tests", "source-" + Guid.NewGuid().ToString("N"));
        _sandboxBasePath = Path.Combine(Path.GetTempPath(), "MCPServer.Workspace.Tests", "sandboxes-" + Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(Path.GetTempPath(), "MCPServer.Workspace.Tests", "workspace-" + Guid.NewGuid().ToString("N"), "workspace.db");
        Directory.CreateDirectory(Path.Combine(_sourceRootPath, "src"));
        File.WriteAllText(Path.Combine(_sourceRootPath, "src", "Program.cs"), "Console.WriteLine(\"Hello from source\");\n");

        _options = new McpWorkspaceOptions
        {
            ApprovalToken = "approved",
            SandboxBasePath = _sandboxBasePath,
            Sqlite =
            {
                DatabasePath = _databasePath,
                EnsureCreatedOnUse = true
            },
            Roots =
            [
                new McpWorkspaceRootOptions
                {
                    Name = "workspace",
                    Path = _sourceRootPath,
                    AllowWrite = true
                }
            ]
        };

        var registry = new MCPServer.Workspace.Stores.SqliteWorkspaceSandboxRegistry(_options);
        _sandboxManager = new DefaultWorkspaceSandboxManager(_options, registry);
        _rootCatalog = new DefaultWorkspaceRootCatalog(_options, _sandboxManager);
        var pathResolver = new DefaultWorkspacePathResolver(_rootCatalog);
        _fileService = new DefaultWorkspaceFileService(_options, _rootCatalog, pathResolver, new WorkspacePatchApplier());
    }

    [Fact]
    public async Task CreateAsync_Creates_Sandbox_And_Makes_It_Available_As_A_Root()
    {
        var createdResult = await _sandboxManager.CreateAsync("workspace", "sandbox-one", "approved", CancellationToken.None);
        var created = TestFin.Success(createdResult);

        Assert.Equal("sandbox-one", created.Name);
        Assert.Equal("sandbox", created.Kind);
        Assert.True(created.Exists);
        Assert.Equal("workspace", created.SourceRootName);

        var resolvedRoot = TestFin.Success(_rootCatalog.FindRoot("sandbox-one"));
        Assert.Equal(created.Path, resolvedRoot.Path);
        Assert.Equal("sandbox", resolvedRoot.Kind);

        var readResult = await _fileService.ReadFileAsync("sandbox-one", Path.Combine("src", "Program.cs"), CancellationToken.None);
        var read = TestFin.Success(readResult);
        Assert.Contains("Hello from source", read.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteAsync_Removes_Sandbox_From_Catalog_And_Disk()
    {
        var created = TestFin.Success(await _sandboxManager.CreateAsync("workspace", "sandbox-two", "approved", CancellationToken.None));
        Assert.True(Directory.Exists(created.Path));

        var deleted = TestFin.Success(await _sandboxManager.DeleteAsync("sandbox-two", "approved", CancellationToken.None));
        Assert.False(deleted.Exists);
        Assert.False(Directory.Exists(created.Path));

        var lookup = TestFin.Failure(_rootCatalog.FindRoot("sandbox-two"));
        Assert.Contains("was not found", lookup.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_Rejects_Wrong_Approval_Token()
    {
        var result = await _sandboxManager.CreateAsync("workspace", "sandbox-three", "wrong", CancellationToken.None);

        var error = TestFin.Failure(result);
        Assert.Contains("approval token", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sandbox_Registry_Persists_Across_Manager_Instances()
    {
        var created = TestFin.Success(await _sandboxManager.CreateAsync("workspace", "sandbox-persisted", "approved", CancellationToken.None));

        var registry = new MCPServer.Workspace.Stores.SqliteWorkspaceSandboxRegistry(_options);
        var secondManager = new DefaultWorkspaceSandboxManager(_options, registry);
        var secondRootCatalog = new DefaultWorkspaceRootCatalog(_options, secondManager);

        var lookup = TestFin.Success(secondRootCatalog.FindRoot("sandbox-persisted"));
        Assert.Equal(created.Path, lookup.Path);
        Assert.Equal("sandbox", lookup.Kind);

        var deleted = TestFin.Success(await secondManager.DeleteAsync("sandbox-persisted", "approved", CancellationToken.None));
        Assert.False(deleted.Exists);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sourceRootPath))
        {
            Directory.Delete(_sourceRootPath, recursive: true);
        }

        if (Directory.Exists(_sandboxBasePath))
        {
            Directory.Delete(_sandboxBasePath, recursive: true);
        }

        var databaseDirectory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory) && Directory.Exists(databaseDirectory))
        {
            Directory.Delete(databaseDirectory, recursive: true);
        }
    }
}
