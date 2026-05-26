using MCPServer.Client.Authorization;
using Xunit;

namespace MCPServer.UnitTests.Client;

public sealed class McpWwwAuthenticateParserTests
{
    [Fact]
    public void TryParse_Extracts_ResourceMetadata_And_Scope()
    {
        var challenge = McpWwwAuthenticateParser.TryParse(new[]
        {
            @"Bearer resource_metadata=""https://mcp.example.com/.well-known/oauth-protected-resource"", scope=""files:read"""
        });

        Assert.NotNull(challenge);
        Assert.Equal("Bearer", challenge!.Scheme);
        Assert.Equal("files:read", challenge.Scope);
        Assert.Equal(new Uri("https://mcp.example.com/.well-known/oauth-protected-resource"), challenge.ResourceMetadataUri);
    }
}
