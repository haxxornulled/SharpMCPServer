using MCPServer.Infrastructure.Mcp.Http;
using MCPServer.Infrastructure.Mcp.Http.Authorization;
using Xunit;

namespace MCPServer.UnitTests.Mcp.Http.Authorization;

public sealed class McpProtectedResourceMetadataProviderTests
{
    [Fact]
    public void CreateDocument_Uses_Configured_Auth_Servers_And_Scopes()
    {
        var options = new StreamableHttpMcpTransportOptions
        {
            Path = "/mcp/",
            Authorization = new StreamableHttpMcpAuthorizationOptions
            {
                Enabled = true,
                AuthorizationServers =
                [
                    "https://auth.example.com/",
                    "https://backup.example.com/"
                ],
                RequiredScopes =
                [
                    "files:read",
                    "files:write"
                ]
            }
        };

        var provider = new McpProtectedResourceMetadataProvider(options);
        var document = provider.CreateDocument(new Uri("http://127.0.0.1:8080/mcp/"));

        Assert.Equal("http://127.0.0.1:8080/mcp/", document.Resource);
        Assert.Equal(new[] { "https://auth.example.com/", "https://backup.example.com/" }, document.AuthorizationServers);
        Assert.Equal(new[] { "files:read", "files:write" }, document.ScopesSupported);
    }

    [Fact]
    public void GetResourceMetadataUri_Uses_WellKnown_Endpoint()
    {
        var provider = new McpProtectedResourceMetadataProvider(new StreamableHttpMcpTransportOptions());

        var metadataUri = provider.GetResourceMetadataUri(new Uri("http://127.0.0.1:8080/mcp/"));

        Assert.Equal(new Uri("http://127.0.0.1:8080/.well-known/oauth-protected-resource"), metadataUri);
    }
}
