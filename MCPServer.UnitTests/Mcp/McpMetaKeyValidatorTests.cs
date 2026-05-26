using System.Text.Json;
using MCPServer.Domain.Mcp;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class McpMetaKeyValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("progressToken")]
    [InlineData("trace_id")]
    [InlineData("com.example/cache-key")]
    [InlineData("io.modelcontextprotocol/related-task")]
    public void IsValid_Accepts_Mcp_Meta_Key_Shapes(string key)
    {
        Assert.True(McpMetaKeyValidator.IsValid(key));
    }

    [Theory]
    [InlineData("-")]
    [InlineData("_bad")]
    [InlineData("bad_")]
    [InlineData("com..example/key")]
    [InlineData("1com.example/key")]
    [InlineData("com.example/key!")]
    [InlineData("com.example/key/again")]
    public void IsValid_Rejects_Invalid_Mcp_Meta_Key_Shapes(string key)
    {
        Assert.False(McpMetaKeyValidator.IsValid(key));
    }

    [Fact]
    public void TryValidateObjectKeys_Rejects_Invalid_Meta_Property_Name()
    {
        using var document = JsonDocument.Parse("{\"bad key\":true}");

        Assert.False(McpMetaKeyValidator.TryValidateObjectKeys(document.RootElement, out var error));
        Assert.Contains("_meta key", error, StringComparison.OrdinalIgnoreCase);
    }
}
