using System.Text.Json;
using Autofac.Features.Indexed;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp.Tools;
using MCPServer.Application.Mcp;
using MCPServer.Domain.Mcp;
using MCPServer.Infrastructure.Mcp.JsonSchema;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class McpToolRegistryTests
{
    [Fact]
    public void Registry_Allows_Tool_Description_To_Be_Absent()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","additionalProperties":false}""");
        var tool = new TestTool(new McpToolDescriptor
        {
            Name = "test.no_description",
            InputSchema = schema.RootElement.Clone()
        });

        var registry = new McpToolRegistry(new IMcpTool[] { tool }, new TestToolIndex(tool), new McpToolRegistryOptions(), new JsonSchemaNetToolArgumentValidator());

        var listed = TestFin.Success(registry.ListTools(string.Empty));
        Assert.Single(listed.Tools);
        Assert.Equal("test.no_description", listed.Tools[0].Name);
    }

    [Fact]
    public void Registry_Rejects_Tool_InputSchema_Without_Root_Object_Type()
    {
        using var schema = JsonDocument.Parse("""{"properties":{}}""");
        var tool = new TestTool(new McpToolDescriptor
        {
            Name = "test.bad_schema",
            Description = "Bad schema test",
            InputSchema = schema.RootElement.Clone()
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new McpToolRegistry(new IMcpTool[] { tool }, new TestToolIndex(tool), new McpToolRegistryOptions(), new JsonSchemaNetToolArgumentValidator()));

        Assert.Contains("root type", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("object", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_Rejects_Tool_OutputSchema_Without_Root_Object_Type()
    {
        using var inputSchema = JsonDocument.Parse("""{"type":"object","additionalProperties":false}""");
        using var outputSchema = JsonDocument.Parse("""{"oneOf":[{"type":"object"},{"type":"object"}]}""");
        var tool = new TestTool(new McpToolDescriptor
        {
            Name = "test.bad_output_schema",
            Description = "Bad output schema test",
            InputSchema = inputSchema.RootElement.Clone(),
            OutputSchema = outputSchema.RootElement.Clone()
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new McpToolRegistry(new IMcpTool[] { tool }, new TestToolIndex(tool), new McpToolRegistryOptions(), new JsonSchemaNetToolArgumentValidator()));

        Assert.Contains("outputSchema", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("root type", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("object", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuiltIn_ClientFeature_Tool_OutputSchemas_Have_Object_Roots()
    {
        var tools = new IMcpTool[]
        {
            new ClientSampleTool(() => new NoOpClientFeatureInvoker()),
            new ClientElicitFormTool(() => new NoOpClientFeatureInvoker()),
            new ClientElicitUrlTool(() => new NoOpClientFeatureInvoker())
        };

        foreach (var tool in tools)
        {
            Assert.True(tool.Descriptor.OutputSchema.HasValue, $"{tool.Descriptor.Name} should declare an output schema.");
            var schema = tool.Descriptor.OutputSchema.Value;
            Assert.Equal(JsonValueKind.Object, schema.ValueKind);
            Assert.True(schema.TryGetProperty("type", out var typeProperty), $"{tool.Descriptor.Name} output schema should declare a root type.");
            Assert.Equal("object", typeProperty.GetString());
        }
    }


    [Fact]
    public void Registry_Rejects_Invalid_Tool_InputSchema_At_Registration_Time()
    {
        using var schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "name": { "type": 42 }
          }
        }
        """);
        var tool = new TestTool(new McpToolDescriptor
        {
            Name = "test.invalid_json_schema",
            Description = "Invalid schema test",
            InputSchema = schema.RootElement.Clone()
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new McpToolRegistry(new IMcpTool[] { tool }, new TestToolIndex(tool), new McpToolRegistryOptions(), new JsonSchemaNetToolArgumentValidator()));

        Assert.Contains("JSON Schema", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_Rejects_Tool_Icon_With_Unsafe_Source_Scheme()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","additionalProperties":false}""");
        var tool = new TestTool(new McpToolDescriptor
        {
            Name = "test.bad_icon",
            InputSchema = schema.RootElement.Clone(),
            Icons =
            [
                new McpIcon
                {
                    Src = "file:///tmp/icon.png",
                    MimeType = "image/png"
                }
            ]
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new McpToolRegistry(new IMcpTool[] { tool }, new TestToolIndex(tool), new McpToolRegistryOptions(), new JsonSchemaNetToolArgumentValidator()));

        Assert.Contains("icon", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("src", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_Allows_Tool_Icon_With_Https_Source()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","additionalProperties":false}""");
        var tool = new TestTool(new McpToolDescriptor
        {
            Name = "test.good_icon",
            InputSchema = schema.RootElement.Clone(),
            Icons =
            [
                new McpIcon
                {
                    Src = "https://example.com/icon.png",
                    MimeType = "image/png",
                    Theme = "dark"
                }
            ]
        });

        var registry = new McpToolRegistry(new IMcpTool[] { tool }, new TestToolIndex(tool), new McpToolRegistryOptions(), new JsonSchemaNetToolArgumentValidator());

        var listed = TestFin.Success(registry.ListTools(string.Empty));
        Assert.Single(listed.Tools);
        Assert.Equal("https://example.com/icon.png", listed.Tools[0].Icons?[0].Src);
    }

    [Fact]
    public void Registry_Uses_Opaque_Tools_List_Cursors_And_Rejects_Client_Guesses()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","additionalProperties":false}""");
        var first = new TestTool(new McpToolDescriptor
        {
            Name = "test.first",
            InputSchema = schema.RootElement.Clone()
        });
        var second = new TestTool(new McpToolDescriptor
        {
            Name = "test.second",
            InputSchema = schema.RootElement.Clone()
        });

        var registry = new McpToolRegistry(
            new IMcpTool[] { first, second },
            new TestToolIndex(first, second),
            new McpToolRegistryOptions { ToolListPageSize = 1 },
            new JsonSchemaNetToolArgumentValidator());

        var firstPage = TestFin.Success(registry.ListTools(string.Empty));
        Assert.Single(firstPage.Tools);
        Assert.Equal("test.first", firstPage.Tools[0].Name);
        Assert.NotNull(firstPage.NextCursor);
        Assert.NotEqual("1", firstPage.NextCursor);

        var secondPage = TestFin.Success(registry.ListTools(firstPage.NextCursor));
        Assert.Single(secondPage.Tools);
        Assert.Equal("test.second", secondPage.Tools[0].Name);
        Assert.Null(secondPage.NextCursor);

        var invalidCursor = TestFin.Failure(registry.ListTools("1"));
        Assert.Contains("cursor", invalidCursor.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestTool : IMcpTool
    {
        public TestTool(McpToolDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public McpToolDescriptor Descriptor { get; }

        public ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
        {
            return new ValueTask<Fin<ToolCallResult>>(Fin.Succ<ToolCallResult>(ToolCallResult.Text("ok")));
        }
    }

    private sealed class NoOpClientFeatureInvoker : IMcpClientFeatureInvoker
    {
        public ValueTask<Fin<JsonElement>> CreateMessageAsync(CreateMessageRequestParams parameters, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse("""{"role":"assistant","content":[]}""");
            return new ValueTask<Fin<JsonElement>>(Fin.Succ(document.RootElement.Clone()));
        }

        public ValueTask<Fin<JsonElement>> ElicitFormAsync(ElicitRequestFormParams parameters, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse("""{"action":"accept","content":{}}""");
            return new ValueTask<Fin<JsonElement>>(Fin.Succ(document.RootElement.Clone()));
        }

        public ValueTask<Fin<JsonElement>> ElicitUrlAsync(ElicitRequestUrlParams parameters, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse("""{"action":"accept","content":{}}""");
            return new ValueTask<Fin<JsonElement>>(Fin.Succ(document.RootElement.Clone()));
        }
    }

    private sealed class TestToolIndex : IIndex<string, IMcpTool>
    {
        private readonly Dictionary<string, IMcpTool> _tools;
        private readonly IMcpTool _fallback;

        public TestToolIndex(params IMcpTool[] tools)
        {
            if (tools.Length == 0)
            {
                throw new ArgumentException("At least one test tool is required.", nameof(tools));
            }

            _tools = new Dictionary<string, IMcpTool>(StringComparer.Ordinal);
            _fallback = tools[0];
            for (var i = 0; i < tools.Length; i++)
            {
                _tools.Add(tools[i].Descriptor.Name, tools[i]);
            }
        }

        public IMcpTool this[string key] => _tools.TryGetValue(key, out var tool)
            ? tool
            : throw new KeyNotFoundException(key);

        public bool TryGetValue(string key, out IMcpTool value)
        {
            if (_tools.TryGetValue(key, out var tool))
            {
                value = tool;
                return true;
            }

            value = _fallback;
            return false;
        }
    }
}
