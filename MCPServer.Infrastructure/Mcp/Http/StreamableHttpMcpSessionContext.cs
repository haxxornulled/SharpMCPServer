using Autofac;
using MCPServer.Application.Mcp.Interfaces;

namespace MCPServer.Infrastructure.Mcp.Http;

public sealed class StreamableHttpMcpSessionContext : IDisposable
{
    private readonly ILifetimeScope _lifetimeScope;
    private readonly IStreamableHttpMcpSessionTransport _transport;
    private int _disposed;
    private bool _started;

    public StreamableHttpMcpSessionContext(
        ILifetimeScope lifetimeScope,
        IStreamableHttpMcpSessionTransport transport)
    {
        ArgumentNullException.ThrowIfNull(lifetimeScope);
        ArgumentNullException.ThrowIfNull(transport);

        _lifetimeScope = lifetimeScope;
        _transport = transport;
    }

    public string SessionId => _transport.SessionId ?? string.Empty;

    public IStreamableHttpMcpSessionTransport Transport => _transport;

    public IMcpRequestDispatcher Dispatcher => _lifetimeScope.Resolve<IMcpRequestDispatcher>();

    public IMcpToolRegistry ToolRegistry => _lifetimeScope.Resolve<IMcpToolRegistry>();

    public string StartSession()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (_started)
        {
            throw new InvalidOperationException("The MCP HTTP session has already been started.");
        }

        _transport.StartSession();
        _started = true;

        return SessionId;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (_started)
            {
                _transport.TerminateSession();
            }
        }
        finally
        {
            _lifetimeScope.Dispose();
        }
    }
}
