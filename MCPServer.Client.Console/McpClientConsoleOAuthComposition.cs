using Autofac;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Client.Authorization;
using MCPServer.Client.Infrastructure.Authorization;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleOAuthComposition
{
    public static Fin<McpClientConsoleOAuthScope> CreateScope(ConsoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var oauthOptions = new McpOAuthAuthorizationProviderOptions
        {
            ClientName = options.OAuthClientName ?? "MCP Server Client Console",
            ClientUri = options.OAuthClientUri,
            ClientId = options.OAuthClientId,
            ClientIdMetadataDocumentUri = options.OAuthClientIdMetadataDocumentUri,
            RedirectUri = options.OAuthRedirectUri,
            UseDynamicClientRegistration = options.OAuthUseDynamicClientRegistration,
            CallbackPath = options.OAuthCallbackPath ?? "/oauth2/callback/",
            BrowserClientName = options.OAuthClientName ?? "MCP Server Client Console",
            HttpClientName = "mcpserver-client-console-oauth-http"
        };

        HttpClientFactoryScope? httpClientFactoryScope = null;
        IContainer? container = null;
        var builder = new ContainerBuilder();
        try
        {
            httpClientFactoryScope = HttpClientFactoryScope.Create();
            builder.RegisterModule(new McpOAuthAuthorizationModule(oauthOptions, httpClientFactoryScope.Factory));

            container = builder.Build();
            var provider = container.Resolve<IMcpAuthorizationProvider>();
            return Fin.Succ(new McpClientConsoleOAuthScope(container, httpClientFactoryScope, provider));
        }
        catch (Exception ex)
        {
            container?.Dispose();
            httpClientFactoryScope?.Dispose();
            return Fin.Fail<McpClientConsoleOAuthScope>(Error.New(ex.Message));
        }
    }
}

internal sealed class McpClientConsoleOAuthScope : IAsyncDisposable, IDisposable
{
    private readonly IContainer _container;
    private readonly HttpClientFactoryScope _httpClientFactoryScope;
    private bool _disposed;

    internal McpClientConsoleOAuthScope(IContainer container, HttpClientFactoryScope httpClientFactoryScope, IMcpAuthorizationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(httpClientFactoryScope);
        ArgumentNullException.ThrowIfNull(provider);

        _container = container;
        _httpClientFactoryScope = httpClientFactoryScope;
        Provider = provider;
    }

    public IMcpAuthorizationProvider Provider { get; }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        try
        {
            _container.Dispose();
        }
        finally
        {
            _httpClientFactoryScope.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
