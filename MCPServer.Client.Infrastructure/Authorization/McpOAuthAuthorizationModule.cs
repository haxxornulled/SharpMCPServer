using Autofac;
using MCPServer.Client.Authorization;

namespace MCPServer.Client.Infrastructure.Authorization;

public sealed class McpOAuthAuthorizationModule : Module
{
    private readonly McpOAuthAuthorizationProviderOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public McpOAuthAuthorizationModule(McpOAuthAuthorizationProviderOptions options, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    protected override void Load(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RegisterInstance(_options)
            .SingleInstance();

        builder.RegisterInstance(_httpClientFactory)
            .ExternallyOwned();

        builder.RegisterType<McpProcessBrowserLauncher>()
            .As<IMcpBrowserLauncher>()
            .SingleInstance();

        builder.RegisterType<McpOAuthAuthorizationCodeProvider>()
            .As<IMcpAuthorizationProvider>()
            .SingleInstance();
    }
}
