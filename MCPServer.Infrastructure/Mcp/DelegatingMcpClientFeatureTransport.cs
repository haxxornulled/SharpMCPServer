using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure.Mcp.Http;
using MCPServer.Infrastructure.Mcp.Stdio;

namespace MCPServer.Infrastructure.Mcp;

public sealed class DelegatingMcpClientFeatureTransport : IMcpClientFeatureInvoker, IMcpTaskStatusNotifier
{
    private readonly IStdioMcpClientFeatureTransport _stdioTransport;
    private readonly IStreamableHttpMcpSessionTransport _httpTransport;

    public DelegatingMcpClientFeatureTransport(
        IStdioMcpClientFeatureTransport stdioTransport,
        IStreamableHttpMcpSessionTransport httpTransport)
    {
        _stdioTransport = stdioTransport ?? throw new ArgumentNullException(nameof(stdioTransport));
        _httpTransport = httpTransport ?? throw new ArgumentNullException(nameof(httpTransport));
    }

    public ValueTask<Fin<JsonElement>> CreateMessageAsync(CreateMessageRequestParams parameters, CancellationToken cancellationToken)
    {
        return SelectTransport().CreateMessageAsync(parameters, cancellationToken);
    }

    public ValueTask<Fin<JsonElement>> ElicitFormAsync(ElicitRequestFormParams parameters, CancellationToken cancellationToken)
    {
        return SelectTransport().ElicitFormAsync(parameters, cancellationToken);
    }

    public ValueTask<Fin<JsonElement>> ElicitUrlAsync(ElicitRequestUrlParams parameters, CancellationToken cancellationToken)
    {
        return SelectTransport().ElicitUrlAsync(parameters, cancellationToken);
    }

    public void Publish(TaskStatusNotificationParams taskStatus)
    {
        SelectNotifier().Publish(taskStatus);
    }

    private IMcpClientFeatureInvoker SelectTransport()
    {
        if (_stdioTransport.HasActiveConnection && _stdioTransport is IMcpClientFeatureInvoker stdioInvoker)
        {
            return stdioInvoker;
        }

        if (_httpTransport.HasActiveSession)
        {
            return _httpTransport;
        }

        return _stdioTransport as IMcpClientFeatureInvoker
            ?? _httpTransport;
    }

    private IMcpTaskStatusNotifier SelectNotifier()
    {
        if (_stdioTransport.HasActiveConnection && _stdioTransport is IMcpTaskStatusNotifier stdioNotifier)
        {
            return stdioNotifier;
        }

        if (_httpTransport.HasActiveSession)
        {
            return _httpTransport;
        }

        return _stdioTransport as IMcpTaskStatusNotifier
            ?? _httpTransport;
    }
}
