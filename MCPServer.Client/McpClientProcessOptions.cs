using MCPServer.Client.Interfaces;

namespace MCPServer.Client;

public sealed class McpClientProcessOptions
{
    public string ServerExecutablePath { get; init; } = string.Empty;

    public IReadOnlyList<string> ServerArguments { get; init; } = Array.Empty<string>();

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public string ClientName { get; init; } = "mcpserver-client-console";

    public string ClientTitle { get; init; } = "MCP Server Client Console";

    public string ClientVersion { get; init; } = "1.0.0";

    public bool SupportsSampling { get; init; }

    public bool SupportsSamplingTools { get; init; }

    public bool SupportsSamplingContext { get; init; }

    public bool SupportsElicitationForm { get; init; }

    public bool SupportsElicitationUrl { get; init; }

    public bool SupportsTasksList { get; init; }

    public bool SupportsTasksCancel { get; init; }

    public bool SupportsTaskSamplingCreateMessage { get; init; }

    public bool SupportsTaskElicitationCreate { get; init; }

    public IMcpClientSamplingHandler? SamplingRequestHandler { get; init; }

    public IMcpClientElicitationHandler? ElicitationRequestHandler { get; init; }

    public IMcpClientTaskRegistry? ClientTaskRegistry { get; init; }

    public IMcpTaskStatusObserver? TaskStatusObserver { get; init; }

    public int MaxInputFrameBytes { get; init; } = 1_048_576;
}
