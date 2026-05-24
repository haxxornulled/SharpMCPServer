using System.Text.Json;
using Autofac.Features.Indexed;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Application.Mcp;
using MCPServer.Domain.Mcp;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class McpResourceRegistryTests
{
    [Fact]
    public void Registry_Rejects_Invalid_Resource_Uri()
    {
        var resource = new TestResource(new McpResourceDescriptor
        {
            Uri = "not a uri",
            Name = "bad"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new McpResourceRegistry(new IMcpResource[] { resource }, new TestResourceIndex(resource), new McpResourceRegistryOptions()));

        Assert.Contains("uri", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_Rejects_Duplicate_Resource_Uri()
    {
        var first = new TestResource(new McpResourceDescriptor
        {
            Uri = "mcpserver://test/duplicate",
            Name = "first"
        });
        var second = new TestResource(new McpResourceDescriptor
        {
            Uri = "mcpserver://test/duplicate",
            Name = "second"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new McpResourceRegistry(new IMcpResource[] { first, second }, new TestResourceIndex(first), new McpResourceRegistryOptions()));

        Assert.Contains("Duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_Uses_Opaque_Resources_List_Cursors_And_Rejects_Client_Guesses()
    {
        var first = new TestResource(new McpResourceDescriptor
        {
            Uri = "mcpserver://test/first",
            Name = "first"
        });
        var second = new TestResource(new McpResourceDescriptor
        {
            Uri = "mcpserver://test/second",
            Name = "second"
        });

        var registry = new McpResourceRegistry(
            new IMcpResource[] { first, second },
            new TestResourceIndex(first, second),
            new McpResourceRegistryOptions { ResourceListPageSize = 1 });

        var firstPage = TestFin.Success(registry.ListResources(string.Empty));
        Assert.Single(firstPage.Resources);
        Assert.Equal("mcpserver://test/first", firstPage.Resources[0].Uri);
        Assert.NotNull(firstPage.NextCursor);
        Assert.NotEqual("1", firstPage.NextCursor);

        var secondPage = TestFin.Success(registry.ListResources(firstPage.NextCursor));
        Assert.Single(secondPage.Resources);
        Assert.Equal("mcpserver://test/second", secondPage.Resources[0].Uri);
        Assert.Null(secondPage.NextCursor);

        var invalidCursor = TestFin.Failure(registry.ListResources("1"));
        Assert.Contains("cursor", invalidCursor.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_Lists_Empty_Resource_Templates_By_Default()
    {
        var resource = new TestResource(new McpResourceDescriptor
        {
            Uri = "mcpserver://test/only",
            Name = "only"
        });

        var registry = new McpResourceRegistry(
            new IMcpResource[] { resource },
            new TestResourceIndex(resource),
            new McpResourceRegistryOptions());

        var templates = TestFin.Success(registry.ListResourceTemplates());
        Assert.Empty(templates.ResourceTemplates);
    }

    private sealed class TestResource : IMcpResource
    {
        public TestResource(McpResourceDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public McpResourceDescriptor Descriptor { get; }

        public ValueTask<Fin<ResourcesReadResult>> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new ResourcesReadResult
            {
                Contents =
                [
                    new ResourceContent
                    {
                        Uri = Descriptor.Uri,
                        MimeType = "text/plain",
                        Text = "ok"
                    }
                ]
            };

            return new ValueTask<Fin<ResourcesReadResult>>(Fin.Succ<ResourcesReadResult>(result));
        }
    }

    private sealed class TestResourceIndex : IIndex<string, IMcpResource>
    {
        private readonly Dictionary<string, IMcpResource> _resources;
        private readonly IMcpResource _fallback;

        public TestResourceIndex(params IMcpResource[] resources)
        {
            if (resources.Length == 0)
            {
                throw new ArgumentException("At least one test resource is required.", nameof(resources));
            }

            _resources = new Dictionary<string, IMcpResource>(StringComparer.Ordinal);
            _fallback = resources[0];
            for (var i = 0; i < resources.Length; i++)
            {
                _resources.Add(resources[i].Descriptor.Uri, resources[i]);
            }
        }

        public IMcpResource this[string key] => _resources.TryGetValue(key, out var resource)
            ? resource
            : throw new KeyNotFoundException(key);

        public bool TryGetValue(string key, out IMcpResource value)
        {
            if (_resources.TryGetValue(key, out var resource))
            {
                value = resource;
                return true;
            }

            value = _fallback;
            return false;
        }
    }
}
