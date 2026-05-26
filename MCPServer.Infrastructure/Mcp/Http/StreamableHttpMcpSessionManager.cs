using System.Collections.Concurrent;
using System.Net;
using Autofac;
using LanguageExt;
using MCPServer.Application.Mcp;
using Microsoft.Extensions.Logging;

namespace MCPServer.Infrastructure.Mcp.Http;

public sealed class StreamableHttpMcpSessionManager : IStreamableHttpMcpSessionManager
{
    private readonly ILifetimeScope _rootLifetimeScope;
    private readonly ConcurrentDictionary<string, StreamableHttpMcpSessionContext> _sessions = new(StringComparer.Ordinal);
    private readonly ILogger<StreamableHttpMcpSessionManager> _logger;
    private int _disposed;

    public StreamableHttpMcpSessionManager(ILifetimeScope rootLifetimeScope, ILogger<StreamableHttpMcpSessionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(rootLifetimeScope);
        ArgumentNullException.ThrowIfNull(logger);
        _rootLifetimeScope = rootLifetimeScope;
        _logger = logger;
    }

    public Fin<StreamableHttpMcpSessionContext> CreateSession()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var sessionScope = _rootLifetimeScope.BeginLifetimeScope(McpLifetimeScopeTags.Session);
        StreamableHttpMcpSessionContext? session = default;
        try
        {
            session = sessionScope.Resolve<StreamableHttpMcpSessionContext>();
            var sessionId = session.StartSession();

            if (!_sessions.TryAdd(sessionId, session))
            {
                session.Dispose();
                return Fin.Fail<StreamableHttpMcpSessionContext>(LanguageExt.Common.Error.New("The MCP HTTP session could not be registered."));
            }

            _logger.LogInformation("Started MCP HTTP session {SessionId}", sessionId);
            return Fin.Succ(session);
        }
        catch (Exception ex)
        {
            if (session is not null)
            {
                session.Dispose();
            }
            else
            {
                sessionScope.Dispose();
            }

            return Fin.Fail<StreamableHttpMcpSessionContext>(LanguageExt.Common.Error.New($"Failed to create MCP HTTP session: {ex.Message}"));
        }
    }

    public bool TryGetSession(string sessionId, out StreamableHttpMcpSessionContext? session)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            session = default;
            return false;
        }

        return _sessions.TryGetValue(sessionId, out session);
    }

    public bool TryTerminateSession(string sessionId)
    {
        if (!TryGetSession(sessionId, out var session) || session is null)
        {
            return false;
        }

        return TryTerminateSession(session);
    }

    public bool TryValidateSessionRequest(
        StreamableHttpMcpRequest request,
        bool isInitialize,
        out StreamableHttpMcpSessionContext? session,
        out HttpStatusCode statusCode,
        out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionHeader = request.GetHeader(StreamableHttpMcpHeaderNames.SessionId);
        if (isInitialize)
        {
            if (!string.IsNullOrWhiteSpace(sessionHeader))
            {
                session = default;
                statusCode = HttpStatusCode.BadRequest;
                errorMessage = "Initialize requests must not include MCP-Session-Id.";
                return false;
            }

            session = default;
            statusCode = HttpStatusCode.OK;
            errorMessage = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(sessionHeader))
        {
            session = default;
            statusCode = HttpStatusCode.BadRequest;
            errorMessage = "Missing MCP-Session-Id header.";
            return false;
        }

        if (!_sessions.TryGetValue(sessionHeader, out session))
        {
            statusCode = HttpStatusCode.NotFound;
            errorMessage = "Session not found.";
            return false;
        }

        statusCode = HttpStatusCode.OK;
        errorMessage = string.Empty;
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var pair in _sessions)
        {
            if (_sessions.TryRemove(pair.Key, out var session))
            {
                session.Dispose();
            }
        }
    }

    private bool TryTerminateSession(StreamableHttpMcpSessionContext session)
    {
        if (_sessions.TryRemove(session.SessionId, out var removed))
        {
            removed.Dispose();
            _logger.LogInformation("Terminated MCP HTTP session {SessionId}", session.SessionId);
            return true;
        }

        return false;
    }
}
