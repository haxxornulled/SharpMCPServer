namespace MCPServer.Application.Mcp;

public readonly struct McpRequestExecutionScope : IDisposable
{
    private readonly IDisposable? _registration;

    public McpRequestExecutionScope(CancellationToken cancellationToken, IDisposable? registration)
    {
        CancellationToken = cancellationToken;
        _registration = registration;
    }

    public CancellationToken CancellationToken { get; }

    public void Dispose()
    {
        _registration?.Dispose();
    }
}
