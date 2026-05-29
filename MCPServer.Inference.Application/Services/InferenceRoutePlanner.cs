using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Application.Options;

namespace MCPServer.Inference.Application.Services;

public sealed class InferenceRoutePlanner
{
    private readonly InferenceRoutingOptions _options;

    public InferenceRoutePlanner(InferenceRoutingOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public IReadOnlyList<IInferenceClient> PlanCandidates(
        IReadOnlyCollection<IInferenceClient> clients,
        InferenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(request);

        var enabledClients = clients
            .Where(static client => client.Descriptor.Enabled)
            .GroupBy(client => client.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (enabledClients.Count == 0)
        {
            return Array.Empty<IInferenceClient>();
        }

        var orderedProviderIds = BuildProviderOrder(enabledClients, request);
        var orderedClients = new List<IInferenceClient>(orderedProviderIds.Count);

        foreach (var providerId in orderedProviderIds)
        {
            if (enabledClients.TryGetValue(providerId, out var client))
            {
                orderedClients.Add(client);
            }
        }

        var strategy = request.RoutingHint?.Strategy ?? _options.DefaultStrategy;
        if (strategy == InferenceRoutingStrategy.PrimaryOnly && orderedClients.Count > 1)
        {
            return [orderedClients[0]];
        }

        if (strategy == InferenceRoutingStrategy.SecondOpinion)
        {
            return orderedClients.Take(2).ToArray();
        }

        if (strategy == InferenceRoutingStrategy.TandemValidate)
        {
            var tandemClients = orderedClients;
            if (_options.TandemValidationEnabled && !string.IsNullOrWhiteSpace(_options.TandemValidationProviderId))
            {
                var validatorProviderId = _options.TandemValidationProviderId.Trim();
                var nonValidatorClients = orderedClients
                    .Where(client => !string.Equals(client.ProviderId, validatorProviderId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (nonValidatorClients.Count >= _options.TandemCandidateCount)
                {
                    tandemClients = nonValidatorClients;
                }
                else
                {
                    var validatorClient = orderedClients.FirstOrDefault(client => string.Equals(client.ProviderId, validatorProviderId, StringComparison.OrdinalIgnoreCase));
                    if (validatorClient is not null && nonValidatorClients.All(client => !ReferenceEquals(client, validatorClient)))
                    {
                        nonValidatorClients.Add(validatorClient);
                    }

                    tandemClients = nonValidatorClients;
                }
            }

            return tandemClients.Take(_options.TandemCandidateCount).ToArray();
        }

        if (strategy == InferenceRoutingStrategy.FanOutCompare && orderedClients.Count > _options.MaxFanOutCandidates)
        {
            return orderedClients.Take(_options.MaxFanOutCandidates).ToArray();
        }

        return orderedClients;
    }

    private IReadOnlyList<string> BuildProviderOrder(
        IReadOnlyDictionary<string, IInferenceClient> enabledClients,
        InferenceRequest request)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Append(string? providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return;
            }

            var normalized = providerId.Trim();
            if (seen.Add(normalized))
            {
                ordered.Add(normalized);
            }
        }

        Append(request.RoutingHint?.PreferredProviderId);

        var fallbackProviderIds = request.RoutingHint?.FallbackProviderIds;
        if (fallbackProviderIds is not null)
        {
            foreach (var providerId in fallbackProviderIds)
            {
                Append(providerId);
            }
        }

        foreach (var providerId in enabledClients.Values
                     .OrderBy(static client => client.Descriptor.RoutingPriority)
                     .ThenBy(static client => client.ProviderId, StringComparer.OrdinalIgnoreCase)
                     .Select(static client => client.ProviderId))
        {
            Append(providerId);
        }

        return ordered;
    }
}
