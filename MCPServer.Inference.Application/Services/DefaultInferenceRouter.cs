using System.Collections.Concurrent;
using System.Text;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Application.Options;
using Microsoft.Extensions.Logging;

namespace MCPServer.Inference.Application.Services;

public sealed class DefaultInferenceRouter : IInferenceRouter
{
    private readonly IReadOnlyCollection<IInferenceClient> _clients;
    private readonly InferenceRoutePlanner _planner;
    private readonly InferenceRoutingOptions _options;
    private readonly ILogger<DefaultInferenceRouter> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _providerGates = new(StringComparer.OrdinalIgnoreCase);

    public DefaultInferenceRouter(
        IEnumerable<IInferenceClient> clients,
        InferenceRoutePlanner planner,
        InferenceRoutingOptions options,
        ILogger<DefaultInferenceRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(clients);
        _clients = clients
            .GroupBy(client => client.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Fin<InferenceResponse>> GenerateAsync(
        InferenceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = _planner.PlanCandidates(_clients, request);
        if (candidates.Count == 0)
        {
            return Fin.Fail<InferenceResponse>(Error.New("No enabled inference providers are registered."));
        }

        var strategy = request.RoutingHint?.Strategy ?? _options.DefaultStrategy;
        return strategy switch
        {
            InferenceRoutingStrategy.FanOutCompare => await GenerateWithFanOutAsync(candidates, request, cancellationToken).ConfigureAwait(false),
            InferenceRoutingStrategy.TandemValidate => await GenerateWithTandemValidationAsync(candidates, request, cancellationToken).ConfigureAwait(false),
            InferenceRoutingStrategy.SecondOpinion => await GenerateWithSecondOpinionAsync(candidates, request, cancellationToken).ConfigureAwait(false),
            _ => await GenerateSequentiallyAsync(candidates, request, cancellationToken).ConfigureAwait(false)
        };
    }

    private async ValueTask<Fin<InferenceResponse>> GenerateSequentiallyAsync(
        IReadOnlyList<IInferenceClient> candidates,
        InferenceRequest request,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();

        foreach (var client in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await GenerateThroughGateAsync(client, request, cancellationToken).ConfigureAwait(false);
            if (result.IsSucc)
            {
                return result;
            }

            failures.Add(result.Match(
                Succ: static _ => string.Empty,
                Fail: static error => error.Message));
        }

        var message = failures.Count == 0
            ? "All inference providers failed."
            : "All inference providers failed: " + string.Join(" | ", failures);

        return Fin.Fail<InferenceResponse>(Error.New(message));
    }

    private async ValueTask<Fin<InferenceResponse>> GenerateWithFanOutAsync(
        IReadOnlyList<IInferenceClient> candidates,
        InferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return Fin.Fail<InferenceResponse>(Error.New("No inference providers are available for fan-out."));
        }

        using var fanOutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = candidates
            .Select(client => GenerateThroughGateAsync(client, request, fanOutCancellation.Token).AsTask())
            .ToList();

        var failures = new List<string>();
        while (tasks.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(completed);

            var result = await completed.ConfigureAwait(false);
            if (result.IsSucc)
            {
                fanOutCancellation.Cancel();
                return result;
            }

            failures.Add(result.Match(
                Succ: static _ => string.Empty,
                Fail: static error => error.Message));
        }

        var message = failures.Count == 0
            ? "Fan-out inference failed."
            : "Fan-out inference failed: " + string.Join(" | ", failures);

        return Fin.Fail<InferenceResponse>(Error.New(message));
    }

    private async ValueTask<Fin<InferenceResponse>> GenerateWithTandemValidationAsync(
        IReadOnlyList<IInferenceClient> candidates,
        InferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return Fin.Fail<InferenceResponse>(Error.New("No inference providers are available for tandem validation."));
        }

        if (candidates.Count == 1)
        {
            return await GenerateThroughGateAsync(candidates[0], request, cancellationToken).ConfigureAwait(false);
        }

        var tasks = candidates
            .Select(client => GenerateThroughGateAsync(client, request, cancellationToken).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var successes = new List<(IInferenceClient Client, InferenceResponse Response)>();
        var failures = new List<string>();

        for (var i = 0; i < results.Length; i++)
        {
            var result = results[i];
            if (result.IsSucc)
            {
                successes.Add((candidates[i], result.Match(
                    Succ: static value => value,
                    Fail: static _ => throw new InvalidOperationException("Unexpected failure result."))));
                continue;
            }

            failures.Add(result.Match(
                Succ: static _ => string.Empty,
                Fail: static error => error.Message));
        }

        if (successes.Count == 0)
        {
            var message = failures.Count == 0
                ? "Tandem validation inference failed."
                : "Tandem validation inference failed: " + string.Join(" | ", failures);

            return Fin.Fail<InferenceResponse>(Error.New(message));
        }

        var primaryResponse = successes[0].Response;
        if (successes.Count == 1)
        {
            return Fin.Succ(ApplyTandemMetadata(
                primaryResponse,
                successes,
                validationStatus: "skipped",
                validatorProviderId: null,
                validatorModel: null));
        }

        if (_options.TandemValidationEnabled)
        {
            var validatorClient = FindClient(_options.TandemValidationProviderId);
            if (validatorClient is not null && validatorClient.Descriptor.Enabled)
            {
                var validatorRequest = BuildValidationRequest(request, successes);
                if (!string.IsNullOrWhiteSpace(_options.TandemValidationModel))
                {
                    validatorRequest = validatorRequest with { Model = _options.TandemValidationModel };
                }

                var validationResult = await GenerateThroughGateAsync(validatorClient, validatorRequest, cancellationToken).ConfigureAwait(false);
                if (validationResult.IsSucc)
                {
                    return validationResult.Match<Fin<InferenceResponse>>(
                        Succ: response => Fin.Succ(ApplyTandemMetadata(
                            response,
                            successes,
                            validationStatus: "applied",
                            validatorProviderId: validatorClient.ProviderId,
                            validatorModel: response.Model)),
                        Fail: static error => Fin.Fail<InferenceResponse>(Error.New(error.Message)));
                }

                _logger.LogWarning(
                    "Tandem validation failed on provider {ValidatorProviderId}; returning the primary candidate instead.",
                    validatorClient.ProviderId);

                return Fin.Succ(ApplyTandemMetadata(
                    primaryResponse,
                    successes,
                    validationStatus: "validator-failed",
                    validatorProviderId: validatorClient.ProviderId,
                    validatorModel: string.IsNullOrWhiteSpace(_options.TandemValidationModel)
                        ? null
                        : _options.TandemValidationModel));
            }
            else
            {
                _logger.LogWarning(
                    "Tandem validation is enabled but provider {ValidatorProviderId} was not available; returning the primary candidate instead.",
                    _options.TandemValidationProviderId);

                return Fin.Succ(ApplyTandemMetadata(
                    primaryResponse,
                    successes,
                    validationStatus: "validator-unavailable",
                    validatorProviderId: _options.TandemValidationProviderId,
                    validatorModel: string.IsNullOrWhiteSpace(_options.TandemValidationModel)
                        ? null
                        : _options.TandemValidationModel));
            }
        }

        return Fin.Succ(ApplyTandemMetadata(
            primaryResponse,
            successes,
            validationStatus: "skipped",
            validatorProviderId: null,
            validatorModel: null));
    }

    private async ValueTask<Fin<InferenceResponse>> GenerateWithSecondOpinionAsync(
        IReadOnlyList<IInferenceClient> candidates,
        InferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return Fin.Fail<InferenceResponse>(Error.New("No inference providers are available for second opinion routing."));
        }

        var failures = new List<string>();
        for (var primaryIndex = 0; primaryIndex < candidates.Count; primaryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var primaryClient = candidates[primaryIndex];
            var primaryResult = await GenerateThroughGateAsync(primaryClient, request, cancellationToken).ConfigureAwait(false);
            if (primaryResult.IsFail)
            {
                failures.Add(primaryResult.Match(
                    Succ: static _ => string.Empty,
                    Fail: static error => error.Message));
                continue;
            }

            var primaryResponse = primaryResult.Match(
                Succ: static response => response,
                Fail: static _ => throw new InvalidOperationException("Unexpected failure result."));

            if (primaryIndex + 1 >= candidates.Count)
            {
                return Fin.Succ(ApplySecondOpinionMetadata(
                    primaryResponse,
                    primaryClient,
                    primaryResponse,
                    status: "skipped",
                    reviewerClient: null,
                    reviewerResponse: null,
                    reviewError: null));
            }

            var reviewerClient = candidates[primaryIndex + 1];
            var reviewerRequest = BuildSecondOpinionRequest(request, primaryClient, primaryResponse);
            var reviewerResult = await GenerateThroughGateAsync(reviewerClient, reviewerRequest, cancellationToken).ConfigureAwait(false);
            if (reviewerResult.IsSucc)
            {
                var reviewerResponse = reviewerResult.Match(
                    Succ: static response => response,
                    Fail: static _ => throw new InvalidOperationException("Unexpected failure result."));

                return Fin.Succ(ApplySecondOpinionMetadata(
                    reviewerResponse,
                    primaryClient,
                    primaryResponse,
                    status: "applied",
                    reviewerClient: reviewerClient,
                    reviewerResponse: reviewerResponse,
                    reviewError: null));
            }

            var reviewError = reviewerResult.Match(
                Succ: static _ => string.Empty,
                Fail: static error => error.Message);
            _logger.LogWarning(
                "Second opinion review failed on provider {ReviewerProviderId}; returning the primary candidate instead.",
                reviewerClient.ProviderId);

            return Fin.Succ(ApplySecondOpinionMetadata(
                primaryResponse,
                primaryClient,
                primaryResponse,
                status: "review-failed",
                reviewerClient: reviewerClient,
                reviewerResponse: null,
                reviewError: reviewError));
        }

        var message = failures.Count == 0
            ? "Second opinion inference failed."
            : "Second opinion inference failed: " + string.Join(" | ", failures);

        return Fin.Fail<InferenceResponse>(Error.New(message));
    }

    private async ValueTask<Fin<InferenceResponse>> GenerateThroughGateAsync(
        IInferenceClient client,
        InferenceRequest request,
        CancellationToken cancellationToken)
    {
        var gate = _providerGates.GetOrAdd(
            client.ProviderId,
            static (_, state) => new SemaphoreSlim(state.MaxConcurrentRequestsPerProvider, state.MaxConcurrentRequestsPerProvider),
            _options);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _logger.LogDebug("Dispatching inference request to provider {ProviderId}.", client.ProviderId);
            return await client.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private IInferenceClient? FindClient(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return _clients.FirstOrDefault(client => string.Equals(client.ProviderId, providerId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static InferenceRequest BuildValidationRequest(
        InferenceRequest originalRequest,
        IReadOnlyList<(IInferenceClient Client, InferenceResponse Response)> successes)
    {
        var messages = new List<InferenceMessage>
        {
            new(
                InferenceRole.System,
                "You are a strict arbitration model. Select the best final answer from the candidate responses. Return only the final answer text.")
        };

        var prompt = new StringBuilder();
        prompt.AppendLine("Original request transcript:");
        AppendMessages(prompt, originalRequest.Messages);
        prompt.AppendLine();
        prompt.AppendLine("Candidate responses:");

        for (var i = 0; i < successes.Count; i++)
        {
            var (client, response) = successes[i];
            prompt.AppendLine($"Candidate {GetCandidateLabel(i)}");
            prompt.AppendLine($"providerId: {client.ProviderId}");
            prompt.AppendLine($"model: {response.Model}");
            prompt.AppendLine("content:");
            prompt.AppendLine(response.Content);
            prompt.AppendLine();
        }

        prompt.AppendLine("Return only the final answer text and do not mention the comparison process.");
        messages.Add(new InferenceMessage(InferenceRole.User, prompt.ToString()));

        return new InferenceRequest(
            messages,
            originalRequest.Model,
            originalRequest.MaxTokens,
            originalRequest.Temperature,
            originalRequest.RoutingHint,
            originalRequest.Metadata);
    }

    private static InferenceRequest BuildSecondOpinionRequest(
        InferenceRequest originalRequest,
        IInferenceClient primaryClient,
        InferenceResponse primaryResponse)
    {
        var messages = new List<InferenceMessage>(originalRequest.Messages.Count + 3)
        {
            new(
                InferenceRole.System,
                "You are a second opinion model. Review the primary model response in the context of the original transcript. Return only the final answer text and do not mention the review process."),
        };

        messages.AddRange(originalRequest.Messages);
        messages.Add(new InferenceMessage(
            InferenceRole.Assistant,
            $"Primary model response from {primaryClient.ProviderId}/{primaryResponse.Model}:\n{primaryResponse.Content}",
            "second-opinion-primary"));
        messages.Add(new InferenceMessage(
            InferenceRole.User,
            "Give a second opinion on the assistant response above. Return only the final answer text."));

        return new InferenceRequest(
            messages,
            originalRequest.Model,
            originalRequest.MaxTokens,
            originalRequest.Temperature,
            originalRequest.RoutingHint,
            originalRequest.Metadata);
    }

    private static void AppendMessages(StringBuilder builder, IReadOnlyList<InferenceMessage> messages)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            builder.Append('[');
            builder.Append(i);
            builder.Append("] ");
            builder.Append(message.Role.ToString().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(message.Name))
            {
                builder.Append('(');
                builder.Append(message.Name);
                builder.Append(')');
            }

            builder.Append(": ");
            builder.AppendLine(message.Content);
        }
    }

    private static string GetCandidateLabel(int index)
    {
        return index switch
        {
            0 => "A",
            1 => "B",
            2 => "C",
            3 => "D",
            4 => "E",
            5 => "F",
            _ => (index + 1).ToString()
        };
    }

    private static InferenceResponse ApplyTandemMetadata(
        InferenceResponse response,
        IReadOnlyList<(IInferenceClient Client, InferenceResponse Response)> successes,
        string validationStatus,
        string? validatorProviderId,
        string? validatorModel)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (response.Metadata is { Count: > 0 } existingMetadata)
        {
            foreach (var pair in existingMetadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        metadata["tandem.status"] = validationStatus;
        metadata["tandem.candidateCount"] = successes.Count.ToString();
        metadata["tandem.primaryProviderId"] = successes[0].Client.ProviderId;
        metadata["tandem.primaryModel"] = successes[0].Response.Model;
        CopyPerformanceMetadata(metadata, successes[0].Response.Metadata, "tandem.primary.");
        if (successes.Count > 1)
        {
            metadata["tandem.secondaryProviderId"] = successes[1].Client.ProviderId;
            metadata["tandem.secondaryModel"] = successes[1].Response.Model;
            CopyPerformanceMetadata(metadata, successes[1].Response.Metadata, "tandem.secondary.");
        }

        for (var i = 0; i < successes.Count; i++)
        {
            metadata[$"tandem.candidate.{i}.providerId"] = successes[i].Client.ProviderId;
            metadata[$"tandem.candidate.{i}.model"] = successes[i].Response.Model;
            CopyPerformanceMetadata(metadata, successes[i].Response.Metadata, $"tandem.candidate.{i}.");
        }

        if (!string.IsNullOrWhiteSpace(validatorProviderId))
        {
            metadata["tandem.validatorProviderId"] = validatorProviderId;
        }

        if (!string.IsNullOrWhiteSpace(validatorModel))
        {
            metadata["tandem.validatorModel"] = validatorModel;
        }

        if (string.Equals(validationStatus, "applied", StringComparison.OrdinalIgnoreCase))
        {
            CopyPerformanceMetadata(metadata, response.Metadata, "tandem.validator.");
        }

        return response with { Metadata = metadata };
    }

    private static InferenceResponse ApplySecondOpinionMetadata(
        InferenceResponse response,
        IInferenceClient primaryClient,
        InferenceResponse primaryResponse,
        string status,
        IInferenceClient? reviewerClient,
        InferenceResponse? reviewerResponse,
        string? reviewError)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (response.Metadata is { Count: > 0 } existingMetadata)
        {
            foreach (var pair in existingMetadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        metadata["secondOpinion.status"] = status;
        metadata["secondOpinion.primaryProviderId"] = primaryClient.ProviderId;
        metadata["secondOpinion.primaryModel"] = primaryResponse.Model;
        CopyPerformanceMetadata(metadata, primaryResponse.Metadata, "secondOpinion.primary.");

        if (reviewerResponse is not null)
        {
            metadata["secondOpinion.reviewerProviderId"] = reviewerResponse.ProviderId;
            metadata["secondOpinion.reviewerModel"] = reviewerResponse.Model;
            CopyPerformanceMetadata(metadata, reviewerResponse.Metadata, "secondOpinion.reviewer.");
        }
        else if (reviewerClient is not null)
        {
            metadata["secondOpinion.reviewerAttemptedProviderId"] = reviewerClient.ProviderId;
        }

        if (!string.IsNullOrWhiteSpace(reviewError))
        {
            metadata["secondOpinion.reviewError"] = reviewError.Length <= 512 ? reviewError : reviewError[..512];
        }

        return response with { Metadata = metadata };
    }

    private static void CopyPerformanceMetadata(
        IDictionary<string, string> target,
        IReadOnlyDictionary<string, string>? source,
        string prefix)
    {
        if (source is null || source.Count == 0)
        {
            return;
        }

        CopyIfPresent(target, source, prefix + "generationElapsedMilliseconds", "generationElapsedMilliseconds");
        CopyIfPresent(target, source, prefix + "loadDurationMilliseconds", "loadDurationMilliseconds");
        CopyIfPresent(target, source, prefix + "promptEvalDurationMilliseconds", "promptEvalDurationMilliseconds");
        CopyIfPresent(target, source, prefix + "evalDurationMilliseconds", "evalDurationMilliseconds");
        CopyIfPresent(target, source, prefix + "totalDurationMilliseconds", "totalDurationMilliseconds");
        CopyIfPresent(target, source, prefix + "tokensPerSecond", "tokensPerSecond");
        CopyIfPresent(target, source, prefix + "inputTokensPerSecond", "inputTokensPerSecond");
        CopyIfPresent(target, source, prefix + "outputTokensPerSecond", "outputTokensPerSecond");
    }

    private static void CopyIfPresent(
        IDictionary<string, string> target,
        IReadOnlyDictionary<string, string> source,
        string targetKey,
        string sourceKey)
    {
        if (source.TryGetValue(sourceKey, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            target[targetKey] = value;
        }
    }
}
