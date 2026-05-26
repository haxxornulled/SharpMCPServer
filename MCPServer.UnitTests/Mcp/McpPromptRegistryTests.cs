using System.Text.Json;
using Autofac.Features.Indexed;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp;
using MCPServer.Domain.Mcp;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class McpPromptRegistryTests
{
    [Fact]
    public void Registry_Rejects_Missing_Prompt_Name()
    {
        var prompt = new TestPrompt(new McpPromptDescriptor());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new McpPromptRegistry(new IMcpPrompt[] { prompt }, new TestPromptIndex(prompt), new McpPromptRegistryOptions()));

        Assert.Contains("name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_Rejects_Duplicate_Prompt_Name()
    {
        var first = new TestPrompt(new McpPromptDescriptor { Name = "duplicate" });
        var second = new TestPrompt(new McpPromptDescriptor { Name = "duplicate" });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new McpPromptRegistry(new IMcpPrompt[] { first, second }, new TestPromptIndex(first), new McpPromptRegistryOptions()));

        Assert.Contains("Duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_Uses_Opaque_Prompts_List_Cursors_And_Rejects_Client_Guesses()
    {
        var first = new TestPrompt(new McpPromptDescriptor { Name = "first" });
        var second = new TestPrompt(new McpPromptDescriptor { Name = "second" });

        var registry = new McpPromptRegistry(
            new IMcpPrompt[] { first, second },
            new TestPromptIndex(first, second),
            new McpPromptRegistryOptions { PromptListPageSize = 1 });

        var firstPage = TestFin.Success(registry.ListPrompts(string.Empty));
        Assert.Single(firstPage.Prompts);
        Assert.Equal("first", firstPage.Prompts[0].Name);
        Assert.NotNull(firstPage.NextCursor);
        Assert.NotEqual("1", firstPage.NextCursor);

        var secondPage = TestFin.Success(registry.ListPrompts(firstPage.NextCursor));
        Assert.Single(secondPage.Prompts);
        Assert.Equal("second", secondPage.Prompts[0].Name);
        Assert.Null(secondPage.NextCursor);

        var invalidCursor = TestFin.Failure(registry.ListPrompts("1"));
        Assert.Contains("cursor", invalidCursor.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_Rejects_Duplicate_Prompt_Argument_Name()
    {
        var prompt = new TestPrompt(new McpPromptDescriptor
        {
            Name = "prompt",
            Arguments =
            [
                new McpPromptArgument { Name = "value" },
                new McpPromptArgument { Name = "value" }
            ]
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new McpPromptRegistry(new IMcpPrompt[] { prompt }, new TestPromptIndex(prompt), new McpPromptRegistryOptions()));

        Assert.Contains("duplicate argument", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestPrompt : IMcpPrompt
    {
        public TestPrompt(McpPromptDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public McpPromptDescriptor Descriptor { get; }

        public ValueTask<Fin<PromptsGetResult>> GetAsync(JsonElement? arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new PromptsGetResult
            {
                Messages =
                [
                    new PromptMessage
                    {
                        Role = McpRoles.User,
                        Content = new TextPromptContent { Text = "ok" }
                    }
                ]
            };

            return new ValueTask<Fin<PromptsGetResult>>(Fin.Succ<PromptsGetResult>(result));
        }
    }

    private sealed class TestPromptIndex : IIndex<string, IMcpPrompt>
    {
        private readonly Dictionary<string, IMcpPrompt> _prompts;
        private readonly IMcpPrompt _fallback;

        public TestPromptIndex(params IMcpPrompt[] prompts)
        {
            if (prompts.Length == 0)
            {
                throw new ArgumentException("At least one test prompt is required.", nameof(prompts));
            }

            _prompts = new Dictionary<string, IMcpPrompt>(StringComparer.Ordinal);
            _fallback = prompts[0];
            for (var i = 0; i < prompts.Length; i++)
            {
                if (prompts[i].Descriptor.Name is { Length: > 0 } name)
                {
                    _prompts[name] = prompts[i];
                }
            }
        }

        public IMcpPrompt this[string key] => _prompts.TryGetValue(key, out var prompt)
            ? prompt
            : throw new KeyNotFoundException(key);

        public bool TryGetValue(string key, out IMcpPrompt value)
        {
            if (_prompts.TryGetValue(key, out var prompt))
            {
                value = prompt;
                return true;
            }

            value = _fallback;
            return false;
        }
    }
}
