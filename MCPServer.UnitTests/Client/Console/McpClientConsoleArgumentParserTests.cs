using System.Text.Json;
using MCPServer.Client.ConsoleApp;
using Xunit;

namespace MCPServer.UnitTests.Client.Console;

public sealed class McpClientConsoleArgumentParserTests
{
    [Fact]
    public void Parse_Captures_Provider_Shortcut_For_Inference_Generate()
    {
        var options = ConsoleOptions.Parse([
            "--server-path",
            "dotnet",
            "--server-arg",
            "MCPServer.Host.dll",
            "--working-directory",
            ".",
            "--tool",
            "inference.generate",
            "--arguments",
            "{\"prompt\":\"hello\"}",
            "--provider",
            "lmstudio"
        ]);

        Assert.Equal("lmstudio", options.InferenceProviderId);
    }

    [Fact]
    public void BuildGenerateArguments_Inserts_ProviderId_When_Missing()
    {
        var options = ConsoleOptions.Parse([
            "--server-path",
            "dotnet",
            "--server-arg",
            "MCPServer.Host.dll",
            "--working-directory",
            ".",
            "--tool",
            "inference.generate",
            "--arguments",
            "{\"prompt\":\"hello\"}",
            "--provider",
            "lmstudio"
        ]);

        using var argumentsDocument = McpClientConsoleArgumentParser.ParseArguments(options.ToolArgumentsJson, out var suppliedArguments, out var parseError);
        Assert.Null(parseError);
        Assert.NotNull(suppliedArguments);

        using var mergedDocument = McpClientConsoleArgumentParser.BuildGenerateArguments(
            options,
            "inference.generate",
            suppliedArguments,
            out var mergedArguments,
            out var mergeError);

        Assert.Null(mergeError);
        Assert.NotNull(mergedArguments);

        Assert.Equal("lmstudio", mergedArguments!.Value.GetProperty("providerId").GetString());
        Assert.Equal("hello", mergedArguments.Value.GetProperty("prompt").GetString());
    }

    [Fact]
    public void BuildGenerateArguments_Rejects_Conflicting_ProviderId()
    {
        var options = ConsoleOptions.Parse([
            "--server-path",
            "dotnet",
            "--server-arg",
            "MCPServer.Host.dll",
            "--working-directory",
            ".",
            "--tool",
            "inference.generate",
            "--arguments",
            "{\"prompt\":\"hello\",\"providerId\":\"ollama\"}",
            "--provider",
            "lmstudio"
        ]);

        using var argumentsDocument = McpClientConsoleArgumentParser.ParseArguments(options.ToolArgumentsJson, out var suppliedArguments, out var parseError);
        Assert.Null(parseError);

        using var mergedDocument = McpClientConsoleArgumentParser.BuildGenerateArguments(
            options,
            "inference.generate",
            suppliedArguments,
            out var mergedArguments,
            out var mergeError);

        Assert.Null(mergedArguments);
        Assert.NotNull(mergeError);
        Assert.Contains("conflicts with providerId", mergeError, StringComparison.OrdinalIgnoreCase);
    }
}
