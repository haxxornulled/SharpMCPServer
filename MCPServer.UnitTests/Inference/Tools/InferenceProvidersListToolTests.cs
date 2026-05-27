using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Tools.Inference.Tools;
using Xunit;

namespace MCPServer.UnitTests.Inference.Tools;

public sealed class InferenceProvidersListToolTests
{
    [Fact]
    public async Task ExecuteAsync_Returns_Sorted_Provider_Statuses()
    {
        var tool = new InferenceProvidersListTool(
        [
            new FakeInferenceClient("ollama", enabled: true),
            new FakeInferenceClient("lmstudio", enabled: false)
        ]);

        using var argumentsDocument = JsonDocument.Parse("{}");
        var result = await tool.ExecuteAsync(argumentsDocument.RootElement.Clone(), CancellationToken.None);

        Assert.True(result.IsSucc);
        var toolResult = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.False(toolResult.IsError);
        var content = Assert.Single(toolResult.Content);
        var textContent = Assert.IsType<TextToolContent>(content);
        Assert.Contains("Found 2 inference provider(s).", textContent.Text, StringComparison.OrdinalIgnoreCase);

        Assert.True(toolResult.StructuredContent.HasValue);
        var providers = toolResult.StructuredContent.Value.GetProperty("providers");
        Assert.Equal(2, providers.GetArrayLength());
        Assert.Equal("lmstudio", providers[0].GetProperty("providerId").GetString());
        Assert.Equal("ollama", providers[1].GetProperty("providerId").GetString());
        Assert.Equal("disabled", providers[0].GetProperty("status").GetString());
        Assert.Equal("ready", providers[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_Live_Probe_Returns_Probe_Details()
    {
        var tool = new InferenceProvidersListTool(
        [
            new FakeInferenceClient(
                "ollama",
                enabled: true,
                probeResult: InferenceProviderProbeResult.Ready("ollama", "OLLAMA", 200, 17, "http://127.0.0.1:11434/v1/models")),
            new FakeInferenceClient(
                "lmstudio",
                enabled: true,
                probeResult: InferenceProviderProbeResult.Unreachable("lmstudio", "LMSTUDIO", 503, 31, "HTTP 503 (ServiceUnavailable): unavailable", "http://192.168.1.44:1234/v1/models")),
            new FakeInferenceClient("anthropic", enabled: false)
        ]);

        using var argumentsDocument = JsonDocument.Parse("""
        {
          "probe": true,
          "probeTimeoutMilliseconds": 100
        }
        """);

        var result = await tool.ExecuteAsync(argumentsDocument.RootElement.Clone(), CancellationToken.None);

        Assert.True(result.IsSucc);
        var toolResult = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.False(toolResult.IsError);
        Assert.Contains("Probed 3 inference provider(s).", Assert.IsType<TextToolContent>(Assert.Single(toolResult.Content)).Text, StringComparison.OrdinalIgnoreCase);

        Assert.True(toolResult.StructuredContent.HasValue);
        var root = toolResult.StructuredContent.Value;
        Assert.True(root.GetProperty("probed").GetBoolean());
        var providers = root.GetProperty("providers");
        Assert.Equal(3, providers.GetArrayLength());

        var anthropic = providers[0];
        Assert.Equal("anthropic", anthropic.GetProperty("providerId").GetString());
        Assert.Equal("disabled", anthropic.GetProperty("status").GetString());

        var lmstudio = providers[1];
        Assert.Equal("lmstudio", lmstudio.GetProperty("providerId").GetString());
        Assert.Equal("unreachable", lmstudio.GetProperty("status").GetString());
        var lmstudioProbe = lmstudio.GetProperty("probe");
        Assert.Equal("unreachable", lmstudioProbe.GetProperty("status").GetString());
        Assert.Equal(503, lmstudioProbe.GetProperty("httpStatusCode").GetInt32());
        Assert.Equal(31, lmstudioProbe.GetProperty("elapsedMilliseconds").GetInt32());

        var ollama = providers[2];
        Assert.Equal("ollama", ollama.GetProperty("providerId").GetString());
        Assert.Equal("ready", ollama.GetProperty("status").GetString());
        var ollamaProbe = ollama.GetProperty("probe");
        Assert.Equal("ready", ollamaProbe.GetProperty("status").GetString());
        Assert.Equal(200, ollamaProbe.GetProperty("httpStatusCode").GetInt32());
        Assert.Equal(17, ollamaProbe.GetProperty("elapsedMilliseconds").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_Rejects_Unexpected_Arguments()
    {
        var tool = new InferenceProvidersListTool(Array.Empty<IInferenceClient>());
        using var argumentsDocument = JsonDocument.Parse("""
        {
          "unexpected": true
        }
        """);

        var result = await tool.ExecuteAsync(argumentsDocument.RootElement.Clone(), CancellationToken.None);

        Assert.True(result.IsSucc);
        var toolResult = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.True(toolResult.IsError);
        var content = Assert.Single(toolResult.Content);
        var textContent = Assert.IsType<TextToolContent>(content);
        Assert.Contains("accepts only probe and probeTimeoutMilliseconds", textContent.Text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeInferenceClient : IInferenceClient
    {
        private readonly InferenceProviderProbeResult _probeResult;

        public FakeInferenceClient(
            string providerId,
            bool enabled,
            InferenceProviderProbeResult? probeResult = null)
        {
            ProviderId = providerId;
            Descriptor = new InferenceProviderDescriptor(providerId, providerId.ToUpperInvariant(), enabled, SupportsStreaming: false);
            _probeResult = probeResult ?? (enabled
                ? InferenceProviderProbeResult.Ready(providerId, providerId.ToUpperInvariant(), 200, 1)
                : InferenceProviderProbeResult.Disabled(providerId, providerId.ToUpperInvariant()));
        }

        public string ProviderId { get; }

        public InferenceProviderDescriptor Descriptor { get; }

        public ValueTask<InferenceProviderProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_probeResult);
        }

        public ValueTask<Fin<InferenceResponse>> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
