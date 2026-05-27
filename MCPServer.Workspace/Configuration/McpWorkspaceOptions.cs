namespace MCPServer.Workspace.Configuration;

public sealed class McpWorkspaceOptions
{
    public const string ConfigurationSectionName = "McpWorkspace";

    public string ApprovalToken { get; set; } = string.Empty;

    public int MaxReadBytes { get; set; } = 1_048_576;

    public int MaxSearchFiles { get; set; } = 5_000;

    public int MaxSearchResults { get; set; } = 250;

    public int MaxPatchBytes { get; set; } = 1_048_576;

    public string SandboxBasePath { get; set; } = string.Empty;

    public McpWorkspaceSqliteOptions Sqlite { get; set; } = new();

    public List<string> ExcludedDirectoryNames { get; set; } =
    [
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules"
    ];

    public List<McpWorkspaceRootOptions> Roots { get; set; } = [];

    public void Validate()
    {
        if (MaxReadBytes <= 0)
        {
            throw new InvalidOperationException("McpWorkspace:MaxReadBytes must be greater than zero.");
        }

        if (MaxSearchFiles <= 0)
        {
            throw new InvalidOperationException("McpWorkspace:MaxSearchFiles must be greater than zero.");
        }

        if (MaxSearchResults <= 0)
        {
            throw new InvalidOperationException("McpWorkspace:MaxSearchResults must be greater than zero.");
        }

        if (MaxPatchBytes <= 0)
        {
            throw new InvalidOperationException("McpWorkspace:MaxPatchBytes must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(SandboxBasePath))
        {
            SandboxBasePath = Path.GetFullPath(SandboxBasePath.Trim(), AppContext.BaseDirectory);
        }
        else
        {
            SandboxBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MCPServer",
                "workspaces");
        }

        Sqlite.Validate();
    }
}

public sealed class McpWorkspaceRootOptions
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public bool AllowWrite { get; set; } = true;
}

public sealed class McpWorkspaceSqliteOptions
{
    public string DatabasePath { get; set; } = string.Empty;

    public bool EnsureCreatedOnUse { get; set; } = true;

    public void Validate()
    {
        if (!string.IsNullOrWhiteSpace(DatabasePath))
        {
            DatabasePath = Path.GetFullPath(DatabasePath.Trim(), AppContext.BaseDirectory);
            return;
        }

        DatabasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MCPServer",
            "workspace",
            "workspace.db");
    }
}
