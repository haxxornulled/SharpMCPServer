namespace MCPServer.VisualStudio.Workspace;

using System.Runtime.Serialization;

/// <summary>
/// Describes the current Visual Studio workspace state as observed through project query.
/// </summary>
[DataContract]
public sealed class WorkspaceSnapshot
{
    /// <summary>
    /// Gets or sets the active workspace root path selected from the solution or project graph.
    /// </summary>
    [DataMember]
    public string WorkspaceRoot { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the source that produced the workspace root.
    /// </summary>
    [DataMember]
    public string WorkspaceRootSource { get; init; } = "unavailable";

    /// <summary>
    /// Gets or sets the solution path reported by Visual Studio, if one is loaded.
    /// </summary>
    [DataMember]
    public string? SolutionPath { get; init; }

    /// <summary>
    /// Gets or sets the solution file name, if one is loaded.
    /// </summary>
    [DataMember]
    public string? SolutionName { get; init; }

    /// <summary>
    /// Gets or sets the first project path reported by Visual Studio, if any.
    /// </summary>
    [DataMember]
    public string? PrimaryProjectPath { get; init; }

    /// <summary>
    /// Gets or sets the active build configuration reported by Visual Studio, if any.
    /// </summary>
    [DataMember]
    public string? ActiveConfiguration { get; init; }

    /// <summary>
    /// Gets or sets the active build platform reported by Visual Studio, if any.
    /// </summary>
    [DataMember]
    public string? ActivePlatform { get; init; }

    /// <summary>
    /// Gets or sets the number of projects currently reported by Visual Studio.
    /// </summary>
    [DataMember]
    public int ProjectCount { get; init; }

    /// <summary>
    /// Gets or sets the UTC time when the snapshot was captured.
    /// </summary>
    [DataMember]
    public DateTime CapturedAtUtc { get; init; }

    /// <summary>
    /// Gets or sets a human-readable status message for the snapshot.
    /// </summary>
    [DataMember]
    public string StatusMessage { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the snapshot contains an actionable workspace root.
    /// </summary>
    public bool HasWorkspaceRoot => !string.IsNullOrWhiteSpace(this.WorkspaceRoot);
}
