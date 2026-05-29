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
    public void PlanCandidates_TandemValidate_Uses_The_Configured_Tandem_Candidate_Count()
    {
        var planner = new InferenceRoutePlanner(new InferenceRoutingOptions
        {
            DefaultStrategy = InferenceRoutingStrategy.TandemValidate,
            TandemCandidateCount = 2
        });

        var request = new InferenceRequest(
            [
                new InferenceMessage(InferenceRole.User, "hello")
            ],
            RoutingHint: new InferenceRoutingHint(
                InferenceRoutingStrategy.TandemValidate,
                PreferredProviderId: "ollama",
                FallbackProviderIds: ["anthropic"]));

        var clients = new IInferenceClient[]
        {
            new FakeInferenceClient("anthropic", enabled: true),
            new FakeInferenceClient("ollama", enabled: true),
            new FakeInferenceClient("lmstudio", enabled: true)
        };

        var plan = planner.PlanCandidates(clients, request);

        Assert.Equal(2, plan.Count);
        Assert.Equal("ollama", plan[0].ProviderId);
        Assert.Equal("anthropic", plan[1].ProviderId);
    }

    [Fact]
    public void PlanCandidates_TandemValidate_Excludes_The_Configured_Validator_When_Enough_Primary_Providers_Are_Available()
    {
        var planner = new InferenceRoutePlanner(new InferenceRoutingOptions
        {
            DefaultStrategy = InferenceRoutingStrategy.TandemValidate,
            TandemCandidateCount = 2,
            TandemValidationEnabled = true,
            TandemValidationProviderId = "anthropic"
        });

        var request = new InferenceRequest(
            [
                new InferenceMessage(InferenceRole.User, "hello")
            ],
            RoutingHint: new InferenceRoutingHint(
                InferenceRoutingStrategy.TandemValidate,
                PreferredProviderId: "anthropic",
                FallbackProviderIds: ["lmstudio", "ollama"]));

        var clients = new IInferenceClient[]
        {
            new FakeInferenceClient("anthropic", enabled: true),
            new FakeInferenceClient("lmstudio", enabled: true),
            new FakeInferenceClient("ollama", enabled: true)
        };

        var plan = planner.PlanCandidates(clients, request);

        Assert.Equal(2, plan.Count);
        Assert.Equal("lmstudio", plan[0].ProviderId);
        Assert.Equal("ollama", plan[1].ProviderId);
    }

    [Fact]
    public void PlanCandidates_SecondOpinion_Uses_The_First_Two_Enabled_Providers()
    {
        var planner = new InferenceRoutePlanner(new InferenceRoutingOptions
        {
            DefaultStrategy = InferenceRoutingStrategy.SecondOpinion
        });

        var request = new InferenceRequest(
            [
                new InferenceMessage(InferenceRole.User, "hello")
            ],
            RoutingHint: new InferenceRoutingHint(
                InferenceRoutingStrategy.SecondOpinion,
                PreferredProviderId: "ollama",
                FallbackProviderIds: ["anthropic"]));

        var clients = new IInferenceClient[]
        {
            new FakeInferenceClient("anthropic", enabled: true),
            new FakeInferenceClient("ollama", enabled: true),
            new FakeInferenceClient("lmstudio", enabled: true)
        };

        var plan = planner.PlanCandidates(clients, request);

        Assert.Equal(2, plan.Count);
        Assert.Equal("ollama", plan[0].ProviderId);
        Assert.Equal("anthropic", plan[1].ProviderId);
    }

    [Fact]
    public void PlanCandidates_Uses_Routing_Priority_Before_Provider_Id()
    {
        var planner = new InferenceRoutePlanner(new InferenceRoutingOptions
        {
            DefaultStrategy = InferenceRoutingStrategy.PrimaryThenFallback
        });

        var request = new InferenceRequest(
            [
                new InferenceMessage(InferenceRole.User, "hello")
            ]);

        var clients = new IInferenceClient[]
        {
            new FakeInferenceClient("anthropic", enabled: true, routingPriority: 30),
            new FakeInferenceClient("ollama", enabled: true, routingPriority: 10),
            new FakeInferenceClient("lmstudio", enabled: true, routingPriority: 20)
        };

        var plan = planner.PlanCandidates(clients, request);

        Assert.Equal(["ollama", "lmstudio", "anthropic"], plan.Select(client => client.ProviderId));
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
            int routingPriority = 0,
            Func<InferenceRequest, InferenceResponse>? resultFactory = null,
            string? failMessage = null)
        {
            ProviderId = providerId;
            Descriptor = new InferenceProviderDescriptor(providerId, providerId.ToUpperInvariant(), enabled, false, routingPriority);
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
