using Autofac;
using MCPServer.Client.Authorization;
using MCPServer.Client.Infrastructure.Authorization;
using Microsoft.Extensions.Http;
using Xunit;

namespace MCPServer.UnitTests.Client.Infrastructure;

public sealed class McpOAuthAuthorizationModuleTests
{
    [Fact]
    public void Provider_Is_Registered_As_Singleton()
    {
        using var container = BuildContainer();

        var first = container.Resolve<IMcpAuthorizationProvider>();
        var second = container.Resolve<IMcpAuthorizationProvider>();

        Assert.Same(first, second);
    }

    private static IContainer BuildContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new McpOAuthAuthorizationModule(
            new McpOAuthAuthorizationProviderOptions
            {
                ClientName = "MCP Test Client",
                HttpClientName = "oauth-test-client"
            },
            new NullHttpClientFactory()));

        return builder.Build();
    }

    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new HttpClientHandler());
        }
    }
}
