namespace MCPServer.VisualStudio.Vsix;

/// <summary>
/// Identifies the command IDs used by the MCPServer VSIX command table.
/// </summary>
internal static class PackageCommandIds
{
    /// <summary>
    /// Gets the command ID for opening the chat console.
    /// </summary>
    public const int OpenChatConsole = 0x0100;

    /// <summary>
    /// Gets the command ID for opening the host.
    /// </summary>
    public const int OpenHost = 0x0101;

    /// <summary>
    /// Gets the command ID for opening the workspace dashboard.
    /// </summary>
    public const int OpenWorkspaceDashboard = 0x0102;

    /// <summary>
    /// Gets the command ID for the MCPServer top-level menu.
    /// </summary>
    public const int MCPServerMenu = 0x0200;

    /// <summary>
    /// Gets the command ID for the MCPServer menu group.
    /// </summary>
    public const int MCPServerMenuGroup = 0x0201;
}
