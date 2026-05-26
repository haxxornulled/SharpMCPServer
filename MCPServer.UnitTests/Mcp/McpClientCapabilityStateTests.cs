using System.Text.Json;
using MCPServer.Domain.Mcp;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class McpClientCapabilityStateTests
{
    [Fact]
    public void TryRead_Recognizes_Roots_Sampling_Elicitation_And_Tasks()
    {
        using var document = JsonDocument.Parse("""
        {
          "roots": { "listChanged": true },
          "sampling": { "tools": {}, "context": {} },
          "elicitation": { "form": {}, "url": {} },
          "tasks": {
            "list": {},
            "cancel": {},
            "requests": {
              "sampling": { "createMessage": {} },
              "elicitation": { "create": {} }
            }
          }
        }
        """);

        var ok = McpClientCapabilityState.TryRead(document.RootElement, out var state, out var error);

        Assert.True(ok, error);
        Assert.True(state.SupportsRoots);
        Assert.True(state.RootsListChanged);
        Assert.True(state.SupportsSampling);
        Assert.True(state.SamplingSupportsTools);
        Assert.True(state.SamplingSupportsContext);
        Assert.True(state.SupportsElicitation);
        Assert.True(state.ElicitationSupportsForm);
        Assert.True(state.ElicitationSupportsUrl);
        Assert.True(state.SupportsTasks);
        Assert.True(state.TasksSupportsList);
        Assert.True(state.TasksSupportsCancel);
        Assert.True(state.TasksSupportsSamplingCreateMessage);
        Assert.True(state.TasksSupportsElicitationCreate);
    }

    [Fact]
    public void TryRead_Rejects_Known_Capability_When_Not_Object()
    {
        using var document = JsonDocument.Parse("""
        { "roots": true }
        """);

        var ok = McpClientCapabilityState.TryRead(document.RootElement, out _, out var error);

        Assert.False(ok);
        Assert.Contains("roots", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRead_Rejects_Roots_ListChanged_When_Not_Boolean()
    {
        using var document = JsonDocument.Parse("""
        { "roots": { "listChanged": "yes" } }
        """);

        var ok = McpClientCapabilityState.TryRead(document.RootElement, out _, out var error);

        Assert.False(ok);
        Assert.Contains("listChanged", error, StringComparison.OrdinalIgnoreCase);
    }
}
