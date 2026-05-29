using MCPServer.Inference.Infrastructure.Options;
using Xunit;

namespace MCPServer.UnitTests.Inference.Options;

public sealed class McpInferenceOptionsTests
{
    [Fact]
    public void Validate_Allows_Codex_Without_An_ApiKey()
    {
        var options = new McpInferenceOptions();
        options.Providers["codex"] = new McpInferenceProviderOptions
        {
            Enabled = true,
            BaseAddress = "https://api.openai.com/v1/",
            Model = "gpt-5.3-codex",
            HttpClientName = "codex"
        };

        options.Validate();
    }

    [Fact]
    public void Validate_Requires_An_ApiKey_For_OpenAI()
    {
        var options = new McpInferenceOptions();
        options.Providers["openai"] = new McpInferenceProviderOptions
        {
            Enabled = true,
            BaseAddress = "https://api.openai.com/v1/",
            Model = "gpt-5.5",
            HttpClientName = "openai"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("requires ApiKey", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
