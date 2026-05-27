using System.Text.Json;
using System.Linq;
using LanguageExt;
using MCPServer.Client.ConsoleApp;
using MCPServer.Client.Interfaces;
using MCPServer.Domain.Mcp;
using Xunit;

namespace MCPServer.UnitTests.Client.Console;

public sealed class McpClientConsoleChatRunnerTests
{
    [Fact]
    public async Task RunAsync_Sends_A_Message_Transcript_And_Prints_The_Response()
    {
        var options = ConsoleOptions.Parse([
            "--transport",
            "stdio",
            "--server-path",
            "dotnet",
            "--working-directory",
            ".",
            "--chat",
            "--provider",
            "lmstudio",
            "--model",
            "gemma4:latest",
            "--system-prompt",
            "You are concise."
        ]);

        var session = new FakeMcpClientSession();
        using var input = new StringReader("/strategy FanOutCompare\n/fallback lmstudio,ollama\n/prompt\nhello\n/exit\n");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await McpClientConsoleChatRunner.RunAsync(session, options, input, output, error, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, session.InitializeCalls);
        Assert.Equal(1, session.ListToolsCalls);
        Assert.Equal(1, session.CallToolCalls);
        Assert.Equal("inference.generate", session.LastToolName);

        Assert.NotNull(session.LastArguments);
        Assert.True(session.LastArguments!.Value.TryGetProperty("messages", out var messages));
        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.Equal(2, messages.GetArrayLength());
        var messageArray = messages.EnumerateArray().ToArray();
        Assert.Equal("system", messageArray[0].GetProperty("role").GetString());
        Assert.Equal("You are concise.", messageArray[0].GetProperty("content").GetString());
        Assert.Equal("user", messageArray[1].GetProperty("role").GetString());
        Assert.Equal("hello", messageArray[1].GetProperty("content").GetString());
        Assert.Equal("lmstudio", session.LastArguments!.Value.GetProperty("providerId").GetString());
        Assert.Equal("gemma4:latest", session.LastArguments!.Value.GetProperty("model").GetString());
        Assert.Equal("FanOutCompare", session.LastArguments!.Value.GetProperty("strategy").GetString());
        var fallbackProviderIds = session.LastArguments!.Value.GetProperty("fallbackProviderIds");
        Assert.Equal(JsonValueKind.Array, fallbackProviderIds.ValueKind);
        Assert.Equal(2, fallbackProviderIds.GetArrayLength());
        var fallbackArray = fallbackProviderIds.EnumerateArray().ToArray();
        Assert.Equal("lmstudio", fallbackArray[0].GetString());
        Assert.Equal("ollama", fallbackArray[1].GetString());

        var stdout = output.ToString();
        Assert.Contains("Chat mode ready.", stdout, StringComparison.Ordinal);
        Assert.Contains("assistant>", stdout, StringComparison.Ordinal);
        Assert.Contains("strategy=PrimaryThenFallback", stdout, StringComparison.Ordinal);
        Assert.Contains("strategy=FanOutCompare", stdout, StringComparison.Ordinal);
        Assert.Contains("fallback=router default", stdout, StringComparison.Ordinal);
        Assert.Contains("fallback=lmstudio, ollama", stdout, StringComparison.Ordinal);
        Assert.Contains("Transcript state:", stdout, StringComparison.Ordinal);
        Assert.Contains("messages=1", stdout, StringComparison.Ordinal);
        Assert.Contains("[0] system: You are concise.", stdout, StringComparison.Ordinal);
        Assert.Contains("provider=lmstudio", stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RunAsync_Supports_Generic_Tool_Calls_And_Transcript_Compaction()
    {
        var options = ConsoleOptions.Parse([
            "--transport",
            "stdio",
            "--server-path",
            "dotnet",
            "--working-directory",
            ".",
            "--chat",
            "--provider",
            "lmstudio",
            "--model",
            "gemma4:latest",
            "--system-prompt",
            "You are concise."
        ]);

        var session = new FakeMcpClientSession();
        using var input = new StringReader("/tools inference.\n/tool server.info {\"detail\":true}\n/compact keep the important bits\n/prompt\nhello\n/exit\n");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await McpClientConsoleChatRunner.RunAsync(session, options, input, output, error, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, session.InitializeCalls);
        Assert.Equal(1, session.ListToolsCalls);
        Assert.Equal(3, session.CallToolCalls);
        Assert.Equal(["server.info", "inference.generate", "inference.generate"], session.CallToolNames);
        Assert.Contains("Tools exposed by server matching 'inference.':", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("- inference.generate: Generate Inference Response", output.ToString(), StringComparison.Ordinal);

        Assert.NotNull(session.CallArguments[0]);
        Assert.True(session.CallArguments[0]!.Value.TryGetProperty("detail", out var detail));
        Assert.True(detail.GetBoolean());

        Assert.NotNull(session.CallArguments[1]);
        Assert.True(session.CallArguments[1]!.Value.TryGetProperty("prompt", out var compactPrompt));
        Assert.Contains("tool(server.info)", compactPrompt.GetString(), StringComparison.Ordinal);
        Assert.Contains("tool says hi", compactPrompt.GetString(), StringComparison.Ordinal);
        Assert.True(session.CallArguments[1]!.Value.TryGetProperty("systemPrompt", out var compactSystemPrompt));
        Assert.Contains("compress chat transcripts", compactSystemPrompt.GetString(), StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(session.CallArguments[2]);
        Assert.True(session.CallArguments[2]!.Value.TryGetProperty("messages", out var compactedMessages));
        Assert.Equal(JsonValueKind.Array, compactedMessages.ValueKind);
        Assert.Equal(3, compactedMessages.GetArrayLength());
        var compactedMessageArray = compactedMessages.EnumerateArray().ToArray();
        Assert.Equal("system", compactedMessageArray[0].GetProperty("role").GetString());
        Assert.Equal("You are concise.", compactedMessageArray[0].GetProperty("content").GetString());
        Assert.Equal("system", compactedMessageArray[1].GetProperty("role").GetString());
        Assert.Equal("conversation-summary", compactedMessageArray[1].GetProperty("name").GetString());
        Assert.Equal("Compact summary", compactedMessageArray[1].GetProperty("content").GetString());
        Assert.Equal("user", compactedMessageArray[2].GetProperty("role").GetString());
        Assert.Equal("hello", compactedMessageArray[2].GetProperty("content").GetString());

        var stdout = output.ToString();
        Assert.Contains("Calling tool: server.info", stdout, StringComparison.Ordinal);
        Assert.Contains("Compacting transcript...", stdout, StringComparison.Ordinal);
        Assert.Contains("Transcript compacted.", stdout, StringComparison.Ordinal);
        Assert.Contains("summary=present", stdout, StringComparison.Ordinal);
        Assert.Contains("messages=2", stdout, StringComparison.Ordinal);
        Assert.Contains("[1] system(conversation-summary): Compact summary", stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RunAsync_Supports_Workspace_Inspection_And_Edit_Aliases()
    {
        var options = ConsoleOptions.Parse([
            "--transport",
            "stdio",
            "--server-path",
            "dotnet",
            "--working-directory",
            ".",
            "--chat"
        ]);

        var session = new FakeMcpClientSession();
        using var input = new StringReader("/tools workspace.\n/search {\"rootName\":\"workspace\",\"query\":\"README\",\"caseSensitive\":false}\n/read {\"rootName\":\"workspace\",\"relativePath\":\"README.md\"}\n/edit {\"rootName\":\"workspace\",\"relativePath\":\"README.md\",\"patch\":\"@@ -1 +1 @@\\nHello\"}\n/exit\n");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await McpClientConsoleChatRunner.RunAsync(session, options, input, output, error, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, session.InitializeCalls);
        Assert.Equal(1, session.ListToolsCalls);
        Assert.Equal(3, session.CallToolCalls);
        Assert.Equal(["workspace.files.search", "workspace.files.read", "workspace.files.applyPatch"], session.CallToolNames);

        Assert.NotNull(session.CallArguments[0]);
        Assert.Equal("workspace", session.CallArguments[0]!.Value.GetProperty("rootName").GetString());
        Assert.Equal("README", session.CallArguments[0]!.Value.GetProperty("query").GetString());
        Assert.False(session.CallArguments[0]!.Value.GetProperty("caseSensitive").GetBoolean());

        Assert.NotNull(session.CallArguments[1]);
        Assert.Equal("workspace", session.CallArguments[1]!.Value.GetProperty("rootName").GetString());
        Assert.Equal("README.md", session.CallArguments[1]!.Value.GetProperty("relativePath").GetString());

        Assert.NotNull(session.CallArguments[2]);
        Assert.Equal("workspace", session.CallArguments[2]!.Value.GetProperty("rootName").GetString());
        Assert.Equal("README.md", session.CallArguments[2]!.Value.GetProperty("relativePath").GetString());
        Assert.Contains("@@ -1 +1 @@", session.CallArguments[2]!.Value.GetProperty("patch").GetString(), StringComparison.Ordinal);

        var stdout = output.ToString();
        Assert.Contains("Tools exposed by server matching 'workspace.':", stdout, StringComparison.Ordinal);
        Assert.Contains("Calling tool: workspace.files.search", stdout, StringComparison.Ordinal);
        Assert.Contains("- workspace.files.read: Read Workspace File", stdout, StringComparison.Ordinal);
        Assert.Contains("- workspace.files.applyPatch: Apply Patch to Workspace File", stdout, StringComparison.Ordinal);
        Assert.Contains("Calling tool: workspace.files.read", stdout, StringComparison.Ordinal);
        Assert.Contains("Calling tool: workspace.files.applyPatch", stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    private sealed class FakeMcpClientSession : IMcpClientSession
    {
        public int InitializeCalls { get; private set; }

        public int ListToolsCalls { get; private set; }

        public int CallToolCalls { get; private set; }

        public string? LastToolName { get; private set; }

        public JsonElement? LastArguments { get; private set; }

        public List<string> CallToolNames { get; } = [];

        public List<JsonElement?> CallArguments { get; } = [];

        private int _inferenceGenerateCalls;

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<Fin<InitializeResult>> InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InitializeCalls++;

            return ValueTask.FromResult(Fin.Succ(new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new McpImplementationInfo
                {
                    Name = "MCPServer",
                    Version = "1.0.0"
                }
            }));
        }

        public ValueTask<Fin<ToolsListResult>> ListToolsAsync(string? cursor, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListToolsCalls++;

            return ValueTask.FromResult(Fin.Succ(new ToolsListResult
            {
                Tools = CreateTools()
            }));
        }

        public ValueTask<Fin<ToolCallResult>> CallToolAsync(string name, JsonElement? arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallToolCalls++;
            LastToolName = name;
            LastArguments = arguments?.Clone();
            CallToolNames.Add(name);
            CallArguments.Add(arguments?.Clone());

            if (string.Equals(name, "server.info", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(Fin.Succ(ToolCallResult.Text(
                    "tool says hi",
                    structuredContent: CreateStructuredContent("""
                    {
                      "kind": "server.info"
                    }
                    """))));
            }

            if (string.Equals(name, "inference.generate", StringComparison.OrdinalIgnoreCase))
            {
                _inferenceGenerateCalls++;
                return _inferenceGenerateCalls switch
                {
                    1 => ValueTask.FromResult(Fin.Succ(ToolCallResult.Text(
                        "Compact summary",
                        structuredContent: CreateStructuredContent("""
                        {
                          "providerId": "lmstudio",
                          "model": "gemma4:latest",
                          "finishReason": "stop"
                        }
                        """)))),
                    _ => ValueTask.FromResult(Fin.Succ(ToolCallResult.Text(
                        "hi there",
                        structuredContent: CreateStructuredContent("""
                        {
                          "providerId": "lmstudio",
                          "model": "gemma4:latest",
                          "finishReason": "stop"
                        }
                        """))))
                };
            }

            if (string.Equals(name, "workspace.files.read", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(Fin.Succ(ToolCallResult.Text(
                    "Read workspace file.",
                    structuredContent: CreateStructuredContent("""
                    {
                      "rootName": "workspace",
                      "path": "README.md",
                      "relativePath": "README.md",
                      "encoding": "utf-8",
                      "bytesRead": 42,
                      "lineCount": 1,
                      "content": "Hello"
                    }
                    """))));
            }

            if (string.Equals(name, "workspace.files.search", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(Fin.Succ(ToolCallResult.Text(
                    "Search results.",
                    structuredContent: CreateStructuredContent("""
                    {
                      "rootNames": ["workspace"],
                      "query": "README",
                      "caseSensitive": false,
                      "filesScanned": 1,
                      "hitCount": 1,
                      "truncated": false,
                      "hits": [
                        {
                          "rootName": "workspace",
                          "path": "README.md",
                          "lineNumber": 1,
                          "matchStart": 0,
                          "matchLength": 6,
                          "line": "README"
                        }
                      ]
                    }
                    """))));
            }

            if (string.Equals(name, "workspace.files.applyPatch", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(Fin.Succ(ToolCallResult.Text(
                    "Applied patch.",
                    structuredContent: CreateStructuredContent("""
                    {
                      "rootName": "workspace",
                      "path": "README.md",
                      "relativePath": "README.md",
                      "appliedHunks": 1,
                      "addedLines": 1,
                      "removedLines": 1,
                      "bytesWritten": 5
                    }
                    """))));
            }
            return ValueTask.FromResult(Fin.Succ(ToolCallResult.Text($"{name} ok")));
        }

        private static McpToolDescriptor[] CreateTools()
        {
            return
            [
                CreateTool("server.info", "Return server metadata."),
                CreateTool("inference.generate", "Generate Inference Response"),
                CreateTool("workspace.files.search", "Search Workspace Files"),
                CreateTool("workspace.files.read", "Read Workspace File"),
                CreateTool("workspace.files.write", "Write Workspace File"),
                CreateTool("workspace.files.applyPatch", "Apply Patch to Workspace File")
            ];
        }

        private static McpToolDescriptor CreateTool(string name, string description)
        {
            using var document = JsonDocument.Parse("{}");
            return new McpToolDescriptor
            {
                Name = name,
                Description = description,
                InputSchema = document.RootElement.Clone()
            };
        }

        private static JsonElement CreateStructuredContent(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
