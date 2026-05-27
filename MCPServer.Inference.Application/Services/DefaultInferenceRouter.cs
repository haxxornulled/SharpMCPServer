using System.Collections.Concurrent;
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
}
