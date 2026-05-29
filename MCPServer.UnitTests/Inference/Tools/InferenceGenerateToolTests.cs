using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Domain.Mcp;
using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Tools.Inference.Tools;
using Xunit;

namespace MCPServer.UnitTests.Inference.Tools;

public sealed class InferenceGenerateToolTests
{
    [Fact]
    public async Task ExecuteAsync_RoutesPrompt_And_ReturnsStructuredProviderMetadata()
    {
        var router = new FakeInferenceRouter(request =>
        {
            Assert.Single(request.Messages);
            Assert.Equal(InferenceRole.User, request.Messages[0].Role);
            Assert.Equal("hello", request.Messages[0].Content);
            Assert.NotNull(request.RoutingHint);
            Assert.Equal(InferenceRoutingStrategy.PrimaryThenFallback, request.RoutingHint!.Strategy);
            Assert.Equal("lmstudio", request.RoutingHint.PreferredProviderId);
            return new InferenceResponse(
                "lmstudio",
                "local-model",
                "hi there",
                "stop",
                new InferenceUsage(12, 4, 16),
                new Dictionary<string, string>
                {
                    ["providerId"] = "lmstudio"
                });
        });

        var tool = new InferenceGenerateTool(router);
        using var argumentsDocument = JsonDocument.Parse("""
        {
          "prompt": "hello",
          "providerId": "lmstudio"
        }
        """);
        var arguments = argumentsDocument.RootElement.Clone();

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.True(result.IsSucc);

        var toolResult = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.False(toolResult.IsError);
        var content = Assert.Single(toolResult.Content);
        var textContent = Assert.IsType<TextToolContent>(content);
        Assert.Equal("hi there", textContent.Text);

        Assert.True(toolResult.StructuredContent is { });
        var structured = toolResult.StructuredContent!.Value;
        Assert.Equal("lmstudio", structured.GetProperty("providerId").GetString());
        Assert.Equal("local-model", structured.GetProperty("model").GetString());
        Assert.Equal("hi there", structured.GetProperty("content").GetString());
        Assert.Equal("stop", structured.GetProperty("finishReason").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_RoutesPrompt_With_TandemValidate_Strategy()
    {
        var router = new FakeInferenceRouter(request =>
        {
            Assert.NotNull(request.RoutingHint);
            Assert.Equal(InferenceRoutingStrategy.TandemValidate, request.RoutingHint!.Strategy);
            Assert.Equal("lmstudio", request.RoutingHint.PreferredProviderId);
            Assert.Equal(["ollama", "anthropic"], request.RoutingHint.FallbackProviderIds);
            return new InferenceResponse(
                "lmstudio",
                "local-model",
                "hi there",
                "stop");
        });

        var tool = new InferenceGenerateTool(router);
        using var argumentsDocument = JsonDocument.Parse("""
        {
          "prompt": "hello",
          "providerId": "lmstudio",
          "strategy": "TandemValidate",
          "fallbackProviderIds": ["ollama", "anthropic"]
        }
        """);
        var arguments = argumentsDocument.RootElement.Clone();

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.True(result.IsSucc);

        var toolResult = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.False(toolResult.IsError);
        var content = Assert.Single(toolResult.Content);
        var textContent = Assert.IsType<TextToolContent>(content);
        Assert.Equal("hi there", textContent.Text);
    }

    [Fact]
    public async Task ExecuteAsync_RoutesPrompt_With_SecondOpinion_Strategy()
    {
        var router = new FakeInferenceRouter(request =>
        {
            Assert.NotNull(request.RoutingHint);
            Assert.Equal(InferenceRoutingStrategy.SecondOpinion, request.RoutingHint!.Strategy);
            Assert.Equal("lmstudio", request.RoutingHint.PreferredProviderId);
            Assert.Equal(["ollama", "anthropic"], request.RoutingHint.FallbackProviderIds);
            return new InferenceResponse(
                "lmstudio",
                "local-model",
                "hi there",
                "stop");
        });

        var tool = new InferenceGenerateTool(router);
        using var argumentsDocument = JsonDocument.Parse("""
        {
          "prompt": "hello",
          "providerId": "lmstudio",
          "strategy": "SecondOpinion",
          "fallbackProviderIds": ["ollama", "anthropic"]
        }
        """);
        var arguments = argumentsDocument.RootElement.Clone();

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.True(result.IsSucc);

        var toolResult = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.False(toolResult.IsError);
        var content = Assert.Single(toolResult.Content);
        var textContent = Assert.IsType<TextToolContent>(content);
        Assert.Equal("hi there", textContent.Text);
    }

    [Fact]
    public async Task ExecuteAsync_RoutesMessages_And_ReturnsStructuredProviderMetadata()
    {
        var router = new FakeInferenceRouter(request =>
        {
            Assert.Equal(2, request.Messages.Count);
            Assert.Equal(InferenceRole.System, request.Messages[0].Role);
            Assert.Equal("You are concise.", request.Messages[0].Content);
            Assert.Equal(InferenceRole.User, request.Messages[1].Role);
            Assert.Equal("hello", request.Messages[1].Content);
            return new InferenceResponse(
                "ollama",
                "gemma4:latest",
                "hi there",
                "stop",
                new InferenceUsage(10, 4, 14),
                new Dictionary<string, string>
                {
                    ["providerId"] = "ollama"
                });
        });

        var tool = new InferenceGenerateTool(router);
        using var argumentsDocument = JsonDocument.Parse("""
        {
          "messages": [
            {"role": "system", "content": "You are concise."},
            {"role": "user", "content": "hello"}
          ],
          "providerId": "ollama",
          "model": "gemma4:latest"
        }
        """);
        var arguments = argumentsDocument.RootElement.Clone();

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.True(result.IsSucc);

        var toolResult = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.False(toolResult.IsError);
        var content = Assert.Single(toolResult.Content);
        var textContent = Assert.IsType<TextToolContent>(content);
        Assert.Equal("hi there", textContent.Text);
    }

    [Fact]
    public async Task ExecuteAsync_Rejects_Missing_Prompt()
    {
        var tool = new InferenceGenerateTool(new FakeInferenceRouter(_ => throw new InvalidOperationException("not expected")));
        using var argumentsDocument = JsonDocument.Parse("""
        {
          "providerId": "lmstudio"
        }
        """);
        var arguments = argumentsDocument.RootElement.Clone();

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.True(result.IsSucc);
        var toolResult = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.True(toolResult.IsError);
        var content = Assert.Single(toolResult.Content);
        var textContent = Assert.IsType<TextToolContent>(content);
        Assert.Contains("prompt", textContent.Text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeInferenceRouter : IInferenceRouter
    {
        private readonly Func<InferenceRequest, InferenceResponse> _handler;

        public FakeInferenceRouter(Func<InferenceRequest, InferenceResponse> handler)
        {
            _handler = handler;
        }

        public ValueTask<Fin<InferenceResponse>> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Fin.Succ(_handler(request)));
        }
    }
}
