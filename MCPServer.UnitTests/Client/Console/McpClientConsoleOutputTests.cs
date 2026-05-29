using System.Text.Json;
using MCPServer.Client.ConsoleApp;
using MCPServer.Domain.Mcp;
using Xunit;

namespace MCPServer.UnitTests.Client.Console;

public sealed class McpClientConsoleOutputTests
{
    [Fact]
    public void WriteToolResultBody_Appends_A_Start_Prompt_When_Inference_Error_Indicates_Connection_Refusal()
    {
        var result = ToolCallResult.Text(
            "All inference providers failed: Inference provider 'lmstudio' failed: No connection could be made because the target machine actively refused it. (127.0.0.1:1234)",
            isError: true);

        using var output = new StringWriter();

        McpClientConsoleOutput.WriteToolResultBody(output, result);

        var text = output.ToString();
        Assert.Contains("All inference providers failed:", text, StringComparison.Ordinal);
        Assert.Contains("Hint: start the configured provider process, then retry.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteToolResultBody_Appends_A_Start_Prompt_When_All_Probed_Providers_Are_Unreachable()
    {
        using var structuredContent = JsonDocument.Parse("""
        {
          "count": 2,
          "probed": true,
          "providers": [
            {
              "providerId": "lmstudio",
              "displayName": "LM Studio",
              "enabled": true,
              "supportsStreaming": false,
              "status": "unreachable",
              "probe": {
                "status": "unreachable",
                "httpStatusCode": 503,
                "elapsedMilliseconds": 31,
                "message": "HTTP 503 (ServiceUnavailable): unavailable",
                "endpoint": "http://127.0.0.1:1234/v1/models"
              }
            },
            {
              "providerId": "ollama",
              "displayName": "Ollama",
              "enabled": true,
              "supportsStreaming": false,
              "status": "timeout",
              "probe": {
                "status": "timeout",
                "httpStatusCode": null,
                "elapsedMilliseconds": 1000,
                "message": "Probe timed out after 1000 ms.",
                "endpoint": "http://127.0.0.1:11434/v1/models"
              }
            }
          ]
        }
        """);

        var result = ToolCallResult.Text(
            "Probed 2 inference provider(s).",
            structuredContent: structuredContent.RootElement.Clone());

        using var output = new StringWriter();

        McpClientConsoleOutput.WriteToolResultBody(output, result);

        var text = output.ToString();
        Assert.Contains("Probed 2 inference provider(s).", text, StringComparison.Ordinal);
        Assert.Contains("Structured content:", text, StringComparison.Ordinal);
        Assert.Contains("Hint: start the configured provider process, then retry.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteToolResultBody_Does_Not_Append_A_Start_Prompt_When_At_Least_One_Provider_Is_Ready()
    {
        using var structuredContent = JsonDocument.Parse("""
        {
          "count": 2,
          "probed": true,
          "providers": [
            {
              "providerId": "lmstudio",
              "displayName": "LM Studio",
              "enabled": true,
              "supportsStreaming": false,
              "status": "ready",
              "probe": {
                "status": "ready",
                "httpStatusCode": 200,
                "elapsedMilliseconds": 17,
                "message": null,
                "endpoint": "http://127.0.0.1:1234/v1/models"
              }
            },
            {
              "providerId": "ollama",
              "displayName": "Ollama",
              "enabled": true,
              "supportsStreaming": false,
              "status": "unreachable",
              "probe": {
                "status": "unreachable",
                "httpStatusCode": 503,
                "elapsedMilliseconds": 31,
                "message": "HTTP 503 (ServiceUnavailable): unavailable",
                "endpoint": "http://127.0.0.1:11434/v1/models"
              }
            }
          ]
        }
        """);

        var result = ToolCallResult.Text(
            "Probed 2 inference provider(s).",
            structuredContent: structuredContent.RootElement.Clone());

        using var output = new StringWriter();

        McpClientConsoleOutput.WriteToolResultBody(output, result);

        var text = output.ToString();
        Assert.Contains("Probed 2 inference provider(s).", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hint: start the configured provider process, then retry.", text, StringComparison.Ordinal);
    }
}
