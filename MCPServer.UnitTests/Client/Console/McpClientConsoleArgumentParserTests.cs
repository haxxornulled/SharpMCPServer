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
    public void Parse_Normalizes_WorkspaceRoot_For_Solution_File_Path()
    {
        var solutionPath = Path.Combine(Path.GetTempPath(), "workspace-root-test", "Demo.slnx");
        var options = ConsoleOptions.Parse([
            "--server-path",
            "dotnet",
            "--server-arg",
            "MCPServer.Host.dll",
            "--working-directory",
            ".",
            "--workspace-root",
            solutionPath
        ]);

        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(solutionPath)), options.WorkspaceRoot);
    }

    [Fact]
    public void BuildStdioServerArguments_Prefixes_WorkspaceRoot_Config()
    {
        var options = ConsoleOptions.Parse([
            "--server-path",
            "dotnet",
            "--server-arg",
            "MCPServer.Host.dll",
            "--working-directory",
            ".",
            "--workspace-root",
            "."
        ]);

        var arguments = McpClientConsoleSessionComposition.BuildStdioServerArguments(options);

        Assert.Equal("MCPServer.Host.dll", arguments[0]);
        Assert.Equal("--McpWorkspace:Roots:0:Name=workspace", arguments[1]);
        Assert.StartsWith("--McpWorkspace:Roots:0:Path=", arguments[2], StringComparison.Ordinal);
        Assert.Equal("--McpWorkspace:Roots:0:AllowWrite=true", arguments[3]);
    }

    [Fact]
    public void Parse_Rejects_WorkspaceRoot_In_Http_Mode()
    {
        var exception = Assert.Throws<ArgumentException>(() => ConsoleOptions.Parse([
            "--endpoint",
            "http://127.0.0.1:3011/mcp/",
            "--workspace-root",
            "."
        ]));

        Assert.Contains("--workspace-root can only be used with stdio transport.", exception.Message, StringComparison.Ordinal);
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
