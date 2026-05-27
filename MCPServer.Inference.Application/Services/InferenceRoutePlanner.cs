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

        var orderedProviderIds = BuildProviderOrder(enabledClients.Keys, request);
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

        if (strategy == InferenceRoutingStrategy.FanOutCompare && orderedClients.Count > _options.MaxFanOutCandidates)
        {
            return orderedClients.Take(_options.MaxFanOutCandidates).ToArray();
        }

        return orderedClients;
    }

    private IReadOnlyList<string> BuildProviderOrder(
        IEnumerable<string> providerIds,
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

        foreach (var providerId in providerIds.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            Append(providerId);
        }

        return ordered;
    }
}
