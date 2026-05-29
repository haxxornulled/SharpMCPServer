using LanguageExt;
using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Application.Options;
using MCPServer.Inference.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MCPServer.UnitTests.Inference.Services;

public sealed class DefaultInferenceRouterTests
{
    [Fact]
    public async Task GenerateAsync_TandemValidate_Runs_The_Primary_Models_In_Parallel_And_Uses_The_Validator_For_The_Final_Decision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var primaryOne = new BlockingInferenceClient(
            "lmstudio",
            request => new InferenceResponse(
                "lmstudio",
                "lmstudio-model",
                "candidate-a",
                "stop"));

        var primaryTwo = new BlockingInferenceClient(
            "ollama",
            request => new InferenceResponse(
                "ollama",
                "ollama-model",
                "candidate-b",
                "stop"));

        var validator = new RecordingInferenceClient(
            "anthropic",
            request =>
            {
                Assert.Equal("judge-4", request.Model);
                Assert.Equal(2, request.Messages.Count);
                Assert.Equal(InferenceRole.System, request.Messages[0].Role);
                Assert.Contains("arbitration model", request.Messages[0].Content, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(InferenceRole.User, request.Messages[1].Role);
                Assert.Contains("candidate-a", request.Messages[1].Content, StringComparison.Ordinal);
                Assert.Contains("candidate-b", request.Messages[1].Content, StringComparison.Ordinal);

                return new InferenceResponse(
                    "anthropic",
                    "judge-4",
                    "validated answer",
                    "stop");
            });

        var options = new InferenceRoutingOptions
        {
            DefaultStrategy = InferenceRoutingStrategy.TandemValidate,
            MaxConcurrentRequestsPerProvider = 2,
            TandemCandidateCount = 2,
            TandemValidationEnabled = true,
            TandemValidationProviderId = "anthropic",
            TandemValidationModel = "judge-4"
        };

        var router = new DefaultInferenceRouter(
            [primaryOne, primaryTwo, validator],
            new InferenceRoutePlanner(options),
            options,
            NullLogger<DefaultInferenceRouter>.Instance);

        var request = new InferenceRequest(
            [
                new InferenceMessage(InferenceRole.User, "solve this")
            ],
            Model: "primary-model",
            RoutingHint: new InferenceRoutingHint(
                InferenceRoutingStrategy.TandemValidate,
                PreferredProviderId: "lmstudio",
                FallbackProviderIds: ["ollama"]));

        var generateTask = router.GenerateAsync(request, cancellationToken).AsTask();

        await primaryOne.Started.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        await primaryTwo.Started.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.False(validator.Started.IsCompleted);

        primaryOne.Release();
        primaryTwo.Release();

        var result = await generateTask;
        Assert.True(result.IsSucc);

        var response = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.Equal("anthropic", response.ProviderId);
        Assert.Equal("judge-4", response.Model);
        Assert.Equal("validated answer", response.Content);
        Assert.NotNull(response.Metadata);
        Assert.Equal("applied", response.Metadata!["tandem.status"]);
        Assert.Equal("2", response.Metadata["tandem.candidateCount"]);
        Assert.Equal("anthropic", response.Metadata["tandem.validatorProviderId"]);
        Assert.Equal("judge-4", response.Metadata["tandem.validatorModel"]);
        Assert.Equal("lmstudio", response.Metadata["tandem.primaryProviderId"]);
        Assert.Equal("ollama", response.Metadata["tandem.secondaryProviderId"]);
        Assert.Equal("lmstudio-model", response.Metadata["tandem.candidate.0.model"]);
        Assert.Equal("ollama-model", response.Metadata["tandem.candidate.1.model"]);
    }

    [Fact]
    public async Task GenerateAsync_SecondOpinion_Runs_The_Reviewer_After_The_Primary_Response()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var primaryOne = new BlockingInferenceClient(
            "lmstudio",
            request => new InferenceResponse(
                "lmstudio",
                "lmstudio-model",
                "candidate-a",
                "stop",
                new InferenceUsage(12, 4, 16),
                new Dictionary<string, string>
                {
                    ["generationElapsedMilliseconds"] = "220",
                    ["loadDurationMilliseconds"] = "35",
                    ["tokensPerSecond"] = "72.727",
                    ["inputTokensPerSecond"] = "54.545",
                    ["outputTokensPerSecond"] = "18.182"
                }));

        var reviewer = new RecordingInferenceClient(
            "openai",
            request =>
            {
                Assert.Equal(4, request.Messages.Count);
                Assert.Equal(InferenceRole.System, request.Messages[0].Role);
                Assert.Contains("second opinion model", request.Messages[0].Content, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(InferenceRole.User, request.Messages[1].Role);
                Assert.Equal("solve this", request.Messages[1].Content);
                Assert.Equal(InferenceRole.Assistant, request.Messages[2].Role);
                Assert.Contains("Primary model response from lmstudio/lmstudio-model", request.Messages[2].Content, StringComparison.Ordinal);
                Assert.Contains("candidate-a", request.Messages[2].Content, StringComparison.Ordinal);
                Assert.Equal("second-opinion-primary", request.Messages[2].Name);
                Assert.Equal(InferenceRole.User, request.Messages[3].Role);
                Assert.Contains("second opinion", request.Messages[3].Content, StringComparison.OrdinalIgnoreCase);

                return new InferenceResponse(
                    "openai",
                    "gpt-5.5",
                    "reviewed answer",
                    "stop",
                    new InferenceUsage(20, 8, 28),
                    new Dictionary<string, string>
                    {
                        ["generationElapsedMilliseconds"] = "310",
                        ["loadDurationMilliseconds"] = "42",
                        ["tokensPerSecond"] = "90.323",
                        ["inputTokensPerSecond"] = "64.516",
                        ["outputTokensPerSecond"] = "25.806"
                    });
            });

        var options = new InferenceRoutingOptions
        {
            DefaultStrategy = InferenceRoutingStrategy.SecondOpinion,
            MaxConcurrentRequestsPerProvider = 2
        };

        var router = new DefaultInferenceRouter(
            [primaryOne, reviewer],
            new InferenceRoutePlanner(options),
            options,
            NullLogger<DefaultInferenceRouter>.Instance);

        var request = new InferenceRequest(
            [
                new InferenceMessage(InferenceRole.User, "solve this")
            ],
            RoutingHint: new InferenceRoutingHint(
                InferenceRoutingStrategy.SecondOpinion,
                PreferredProviderId: "lmstudio",
                FallbackProviderIds: ["openai"]));

        var generateTask = router.GenerateAsync(request, cancellationToken).AsTask();

        await primaryOne.Started.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.False(reviewer.Started.IsCompleted);

        primaryOne.Release();
        await reviewer.Started.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        var result = await generateTask;
        Assert.True(result.IsSucc);

        var response = result.Match(
            Succ: static value => value,
            Fail: static error => throw new Xunit.Sdk.XunitException(error.Message));

        Assert.Equal("openai", response.ProviderId);
        Assert.Equal("gpt-5.5", response.Model);
        Assert.Equal("reviewed answer", response.Content);
        Assert.NotNull(response.Metadata);
        Assert.Equal("applied", response.Metadata!["secondOpinion.status"]);
        Assert.Equal("lmstudio", response.Metadata["secondOpinion.primaryProviderId"]);
        Assert.Equal("lmstudio-model", response.Metadata["secondOpinion.primaryModel"]);
        Assert.Equal("openai", response.Metadata["secondOpinion.reviewerProviderId"]);
        Assert.Equal("gpt-5.5", response.Metadata["secondOpinion.reviewerModel"]);
        Assert.Equal("220", response.Metadata["secondOpinion.primary.generationElapsedMilliseconds"]);
        Assert.Equal("35", response.Metadata["secondOpinion.primary.loadDurationMilliseconds"]);
        Assert.Equal("72.727", response.Metadata["secondOpinion.primary.tokensPerSecond"]);
        Assert.Equal("310", response.Metadata["secondOpinion.reviewer.generationElapsedMilliseconds"]);
        Assert.Equal("42", response.Metadata["secondOpinion.reviewer.loadDurationMilliseconds"]);
        Assert.Equal("90.323", response.Metadata["secondOpinion.reviewer.tokensPerSecond"]);
    }

    private sealed class BlockingInferenceClient : IInferenceClient
    {
        private readonly Func<InferenceRequest, InferenceResponse> _responseFactory;
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<InferenceRequest> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingInferenceClient(string providerId, Func<InferenceRequest, InferenceResponse> responseFactory)
        {
            ProviderId = providerId;
            _responseFactory = responseFactory;
            Descriptor = new InferenceProviderDescriptor(providerId, providerId.ToUpperInvariant(), true, false);
        }

        public string ProviderId { get; }

        public InferenceProviderDescriptor Descriptor { get; }

        public Task<InferenceRequest> Started => _started.Task;

        public void Release()
        {
            _release.TrySetResult(true);
        }

        public ValueTask<InferenceProviderProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(InferenceProviderProbeResult.Ready(ProviderId, ProviderId.ToUpperInvariant(), 200, 1));
        }

        public async ValueTask<Fin<InferenceResponse>> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _started.TrySetResult(request);
            await _release.Task.WaitAsync(cancellationToken);
            return Fin.Succ(_responseFactory(request));
        }
    }

    private sealed class RecordingInferenceClient : IInferenceClient
    {
        private readonly Func<InferenceRequest, InferenceResponse> _responseFactory;
        private readonly TaskCompletionSource<InferenceRequest> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingInferenceClient(string providerId, Func<InferenceRequest, InferenceResponse> responseFactory)
        {
            ProviderId = providerId;
            _responseFactory = responseFactory;
            Descriptor = new InferenceProviderDescriptor(providerId, providerId.ToUpperInvariant(), true, false);
        }

        public string ProviderId { get; }

        public InferenceProviderDescriptor Descriptor { get; }

        public Task<InferenceRequest> Started => _started.Task;

        public ValueTask<InferenceProviderProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(InferenceProviderProbeResult.Ready(ProviderId, ProviderId.ToUpperInvariant(), 200, 1));
        }

        public ValueTask<Fin<InferenceResponse>> GenerateAsync(InferenceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _started.TrySetResult(request);
            return ValueTask.FromResult(Fin.Succ(_responseFactory(request)));
        }
    }
}
