using LanguageExt;
using LanguageExt.Common;
using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Application.Options;
using MCPServer.Inference.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPServer.UnitTests.Inference.Services;

public sealed class InferenceRoutePlannerTests
{
    [Fact]
    public void PlanCandidates_PrimaryOnly_Uses_The_First_Enabled_Provider()
    {
        var planner = new InferenceRoutePlanner(new InferenceRoutingOptions
        {
            DefaultStrategy = InferenceRoutingStrategy.PrimaryOnly
        });

        var request = new InferenceRequest(
            [
                new InferenceMessage(InferenceRole.User, "hello")
            ],
            RoutingHint: new InferenceRoutingHint(
                InferenceRoutingStrategy.PrimaryOnly,
                PreferredProviderId: "ollama",
                FallbackProviderIds: ["anthropic"]));

        var clients = new IInferenceClient[]
        {
            new FakeInferenceClient("anthropic", enabled: true),
            new FakeInferenceClient("ollama", enabled: true),
            new FakeInferenceClient("lmstudio", enabled: true)
        };

        var plan = planner.PlanCandidates(clients, request);

        Assert.Single(plan);
        Assert.Equal("ollama", plan[0].ProviderId);
    }

    [Fact]
    public async Task GenerateAsync_Falls_Back_To_The_Second_Provider_When_The_First_Fails()
    {
        var options = new InferenceRoutingOptions
        {
            DefaultStrategy = InferenceRoutingStrategy.PrimaryThenFallback,
            MaxConcurrentRequestsPerProvider = 2
        };

        var router = new DefaultInferenceRouter(
            [
                new FakeInferenceClient("anthropic", enabled: true, failMessage: "anthropic unavailable"),
                new FakeInferenceClient("ollama", enabled: true, resultFactory: request => new InferenceResponse(
                    "ollama",
                    request.Model ?? "test-model",
                    "fallback response",
                    "stop")),
                new FakeInferenceClient("lmstudio", enabled: false)
            ],
            new InferenceRoutePlanner(options),
            options,
            NullLogger<DefaultInferenceRouter>.Instance);

        var result = await router.GenerateAsync(
            new InferenceRequest(
                [
                    new InferenceMessage(InferenceRole.User, "say hello")
                ],
                Model: "test-model"),
            CancellationToken.None);

        Assert.True(result.IsSucc);

        var response = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.Equal("ollama", response.ProviderId);
        Assert.Equal("fallback response", response.Content);
    }

    private sealed class FakeInferenceClient : IInferenceClient
    {
        private readonly Func<InferenceRequest, ValueTask<Fin<InferenceResponse>>> _handler;

        public FakeInferenceClient(
            string providerId,
            bool enabled,
            Func<InferenceRequest, InferenceResponse>? resultFactory = null,
            string? failMessage = null)
        {
            ProviderId = providerId;
            Descriptor = new InferenceProviderDescriptor(providerId, providerId.ToUpperInvariant(), enabled, SupportsStreaming: false);
            _handler = resultFactory is not null
                ? request => ValueTask.FromResult(Fin.Succ(resultFactory(request)))
                : _ => ValueTask.FromResult(Fin.Fail<InferenceResponse>(Error.New(failMessage ?? $"{providerId} failed")));
        }

        public string ProviderId { get; }

        public InferenceProviderDescriptor Descriptor { get; }

        public ValueTask<InferenceProviderProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(InferenceProviderProbeResult.Ready(ProviderId, ProviderId.ToUpperInvariant(), 200, 1));
        }

        public ValueTask<Fin<InferenceResponse>> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _handler(request);
        }
    }
}
