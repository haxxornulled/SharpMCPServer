using System.Collections.Concurrent;
using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;
using MCPServer.Inference.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MCPServer.Inference.Infrastructure.Hosting;

public sealed class LocalInferenceProviderBootstrapService : IHostedService, IAsyncDisposable
{
    private static readonly string[] LocalProviderIds = ["lmstudio", "ollama"];
    private static readonly TimeSpan ReadyProbeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReadyProbeDelay = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyDictionary<string, IInferenceClient> _clients;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly McpInferenceOptions _options;
    private readonly ILocalInferenceProviderLauncher _launcher;
    private readonly ILogger<LocalInferenceProviderBootstrapService> _logger;
    private readonly ConcurrentBag<ILocalInferenceProviderHandle> _ownedHandles = new();

    public LocalInferenceProviderBootstrapService(
        IEnumerable<IInferenceClient> clients,
        IHttpClientFactory httpClientFactory,
        McpInferenceOptions options,
        ILocalInferenceProviderLauncher launcher,
        ILogger<LocalInferenceProviderBootstrapService> logger)
    {
        _clients = (clients ?? throw new ArgumentNullException(nameof(clients)))
            .GroupBy(client => client.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var launchTasks = LocalProviderIds
            .Select(providerId => EnsureLocalProviderAsync(providerId, cancellationToken))
            .ToArray();

        await Task.WhenAll(launchTasks).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopTasks = _ownedHandles
            .Select(handle => handle.StopAsync(cancellationToken).AsTask())
            .ToArray();

        if (stopTasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(stopTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal host shutdown path.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "One or more local inference provider stop hooks failed during shutdown.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task EnsureLocalProviderAsync(string providerId, CancellationToken cancellationToken)
    {
        try
        {
            if (!_options.Providers.TryGetValue(providerId, out var providerOptions) || !providerOptions.Enabled)
            {
                return;
            }

            if (!IsLoopbackBaseAddress(providerOptions.BaseAddress))
            {
                _logger.LogDebug(
                    "Skipping local bootstrap for provider {ProviderId} because base address {BaseAddress} is not loopback.",
                    providerId,
                    providerOptions.BaseAddress);
                return;
            }

            if (!_clients.TryGetValue(providerId, out var client))
            {
                _logger.LogDebug("Skipping local bootstrap for provider {ProviderId} because no inference client is registered.", providerId);
                return;
            }

            var probe = await client.ProbeAsync(cancellationToken).ConfigureAwait(false);
            if (probe.Status is InferenceProviderProbeStatus.Ready or InferenceProviderProbeStatus.Unauthorized)
            {
                _logger.LogInformation(
                    "Local inference provider {ProviderId} is already reachable at {Endpoint}.",
                    providerId,
                    probe.Endpoint ?? providerOptions.BaseAddress);
                return;
            }

            _logger.LogInformation(
                "Local inference provider {ProviderId} is not reachable; starting the managed bootstrap path.",
                providerId);

            var handle = await _launcher.StartAsync(providerId, providerOptions, cancellationToken).ConfigureAwait(false);
            if (handle is null)
            {
                _logger.LogWarning(
                    "Local inference provider {ProviderId} could not be started by the host.",
                    providerId);
                return;
            }

            _ownedHandles.Add(handle);

            var ready = await WaitForReadyAsync(client, providerId, providerOptions.BaseAddress, cancellationToken).ConfigureAwait(false);
            if (ready)
            {
                if (string.Equals(providerId, "ollama", StringComparison.OrdinalIgnoreCase))
                {
                    await ResolveOllamaModelAsync(providerOptions, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "Local inference provider {ProviderId} became reachable at {BaseAddress}.",
                    providerId,
                    providerOptions.BaseAddress);
            }
            else
            {
                _logger.LogWarning(
                    "Local inference provider {ProviderId} did not become reachable within {TimeoutSeconds} seconds; the host will continue and the provider may still finish loading in the background.",
                    providerId,
                    (int)ReadyProbeTimeout.TotalSeconds);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Local inference provider bootstrap for {ProviderId} failed.",
                providerId);
        }
    }

    private async Task<bool> WaitForReadyAsync(
        IInferenceClient client,
        string providerId,
        string baseAddress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < ReadyProbeTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var probe = await client.ProbeAsync(cancellationToken).ConfigureAwait(false);
            if (probe.Status is InferenceProviderProbeStatus.Ready or InferenceProviderProbeStatus.Unauthorized)
            {
                return true;
            }

            _logger.LogDebug(
                "Waiting for local inference provider {ProviderId} at {BaseAddress} to become reachable. Current status: {Status}",
                providerId,
                baseAddress,
                probe.Status);

            await Task.Delay(ReadyProbeDelay, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static bool IsLoopbackBaseAddress(string baseAddress)
    {
        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> ResolveOllamaModelAsync(
        McpInferenceProviderOptions providerOptions,
        CancellationToken cancellationToken)
    {
        using var discoveryTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryTimeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            var requestUri = BuildProviderRequestUri(providerOptions.BaseAddress, "api/tags");
            var httpClient = _httpClientFactory.CreateClient(string.IsNullOrWhiteSpace(providerOptions.HttpClientName)
                ? "ollama"
                : providerOptions.HttpClientName.Trim());

            using var response = await httpClient.GetAsync(requestUri, discoveryTimeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Ollama model discovery returned HTTP {StatusCode} from {RequestUri}.",
                    (int)response.StatusCode,
                    requestUri);
                return string.IsNullOrWhiteSpace(providerOptions.Model)
                    ? null
                    : providerOptions.Model.Trim();
            }

            var payload = await response.Content.ReadAsStringAsync(discoveryTimeoutCts.Token).ConfigureAwait(false);
            var models = LocalInferenceModelDiscovery.ParseLmStudioModels(payload);
            var selectedModel = LocalInferenceModelDiscovery.SelectPreferredModel(models, providerOptions.Model);
            if (string.IsNullOrWhiteSpace(selectedModel))
            {
                return string.IsNullOrWhiteSpace(providerOptions.Model)
                    ? null
                    : providerOptions.Model.Trim();
            }

            if (!string.Equals(providerOptions.Model.Trim(), selectedModel, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Ollama bootstrap resolved configured model '{ConfiguredModel}' to installed model '{ResolvedModel}'.",
                    providerOptions.Model,
                    selectedModel);
            }

            providerOptions.Model = selectedModel;
            return selectedModel;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Ollama model discovery timed out after 10 seconds.");
            return string.IsNullOrWhiteSpace(providerOptions.Model)
                ? null
                : providerOptions.Model.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ollama model discovery failed.");
            return string.IsNullOrWhiteSpace(providerOptions.Model)
                ? null
                : providerOptions.Model.Trim();
        }
    }

    private static Uri BuildProviderRequestUri(string baseAddress, string relativePath)
    {
        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Inference provider base address '{baseAddress}' is not a valid absolute URI.");
        }

        if (relativePath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            baseUri = RemoveOpenAiVersionSegmentIfPresent(baseUri);
        }

        return new Uri(baseUri, relativePath.TrimStart('/'));
    }

    private static Uri RemoveOpenAiVersionSegmentIfPresent(Uri baseUri)
    {
        var path = baseUri.AbsolutePath;
        if (path.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }
        else if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^3];
        }
        else
        {
            return baseUri;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            path = "/";
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = path
        };

        return builder.Uri;
    }
}
