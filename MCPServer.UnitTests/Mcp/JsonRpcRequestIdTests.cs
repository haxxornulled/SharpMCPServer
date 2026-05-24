using System.Text.Json;
using MCPServer.Domain.Mcp;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class JsonRpcRequestIdTests
{
    [Fact]
    public void Missing_WriteTo_Throws_Because_Mcp_Error_Responses_Omit_Unreadable_Id()
    {
        using var output = new MemoryStream();
        using var writer = new Utf8JsonWriter(output);

        var exception = Assert.Throws<InvalidOperationException>(() => JsonRpcRequestId.Missing.WriteTo(writer));

        Assert.Contains("omitted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
