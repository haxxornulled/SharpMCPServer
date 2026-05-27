using System.Buffers;
using System.Text.Json;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Inference.Abstractions.Interfaces;
using MCPServer.Inference.Abstractions.Models;

namespace MCPServer.Tools.Inference.Tools;

public sealed class InferenceProvidersListTool : IMcpTool
{
    private static readonly JsonElement InputSchema = InferenceToolSchemas.CreateProvidersListInputSchema();
    private static readonly JsonElement OutputSchema = InferenceToolSchemas.CreateProvidersListOutputSchema();

    private readonly IReadOnlyList<IInferenceClient> _clients;

    public InferenceProvidersListTool(IEnumerable<IInferenceClient> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        _clients = clients
            .GroupBy(static client => client.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static client => client.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public McpToolDescriptor Descriptor { get; } = new McpToolDescriptor
    {
        Name = InferenceToolNames.ProvidersList,
        Title = "List Inference Providers",
        Description = "Lists configured inference providers and their readiness state.",
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Execution = new McpToolExecution
        {
            TaskSupport = McpToolTaskSupport.Forbidden
        }
    };

    public async ValueTask<Fin<ToolCallResult>> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Object })
        {
            return Fin.Succ<ToolCallResult>(ToolCallResult.Text("inference.providers.list expects an arguments object when supplied.", isError: true));
        }

        var probeRequested = false;
        int? probeTimeout = null;
        if (arguments is { ValueKind: JsonValueKind.Object } supplied)
        {
            foreach (var property in supplied.EnumerateObject())
            {
                if (!property.NameEquals("probe"u8) && !property.NameEquals("probeTimeoutMilliseconds"u8))
                {
                    return Fin.Succ<ToolCallResult>(ToolCallResult.Text("inference.providers.list accepts only probe and probeTimeoutMilliseconds.", isError: true));
                }
            }

            var probeEnabledValue = TryReadOptionalBoolean(supplied, "probe"u8);
            probeTimeout = TryReadOptionalInt32(supplied, "probeTimeoutMilliseconds"u8);

            if (probeTimeout is <= 0)
            {
                return Fin.Succ<ToolCallResult>(ToolCallResult.Text("inference.providers.list requires probeTimeoutMilliseconds to be greater than zero when supplied.", isError: true));
            }

            if (probeTimeout is not null && probeEnabledValue is not true)
            {
                return Fin.Succ<ToolCallResult>(ToolCallResult.Text("inference.providers.list requires probe=true when probeTimeoutMilliseconds is supplied.", isError: true));
            }

            probeRequested = probeEnabledValue is true;
        }

        var entries = probeRequested
            ? await BuildProbedEntriesAsync(_clients, probeTimeout, cancellationToken).ConfigureAwait(false)
            : BuildStaticEntries(_clients);

        var structuredContent = CreateStructuredContent(entries, probeRequested);
        var summary = _clients.Count == 0
            ? "No inference providers are registered."
            : probeRequested
                ? $"Probed {_clients.Count} inference provider(s)."
                : $"Found {_clients.Count} inference provider(s).";

        return Fin.Succ<ToolCallResult>(ToolCallResult.Text(summary, structuredContent: structuredContent));
    }

    private static IReadOnlyList<ProviderListingEntry> BuildStaticEntries(IReadOnlyList<IInferenceClient> clients)
    {
        return clients
            .Select(client =>
            {
                var descriptor = client.Descriptor;
                return new ProviderListingEntry(
                    descriptor.ProviderId,
                    descriptor.DisplayName,
                    descriptor.Enabled,
                    descriptor.SupportsStreaming,
                    descriptor.Enabled ? InferenceProviderProbeStatus.Ready : InferenceProviderProbeStatus.Disabled,
                    null);
            })
            .ToArray();
    }

    private static async ValueTask<IReadOnlyList<ProviderListingEntry>> BuildProbedEntriesAsync(
        IReadOnlyList<IInferenceClient> clients,
        int? probeTimeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var probeTasks = clients.Select(client => ProbeProviderAsync(client, probeTimeoutMilliseconds, cancellationToken)).ToArray();
        return await Task.WhenAll(probeTasks).ConfigureAwait(false);
    }

    private static async Task<ProviderListingEntry> ProbeProviderAsync(
        IInferenceClient client,
        int? probeTimeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var descriptor = client.Descriptor;
        if (!descriptor.Enabled)
        {
            return new ProviderListingEntry(
                descriptor.ProviderId,
                descriptor.DisplayName,
                descriptor.Enabled,
                descriptor.SupportsStreaming,
                InferenceProviderProbeStatus.Disabled,
                null);
        }

        using var timeoutCts = probeTimeoutMilliseconds is > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;

        if (timeoutCts is not null)
        {
            var timeoutMilliseconds = probeTimeoutMilliseconds.GetValueOrDefault();
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        }

        var probeToken = timeoutCts?.Token ?? cancellationToken;
        try
        {
            var probe = await client.ProbeAsync(probeToken).ConfigureAwait(false);
            return new ProviderListingEntry(
                descriptor.ProviderId,
                descriptor.DisplayName,
                descriptor.Enabled,
                descriptor.SupportsStreaming,
                probe.Status,
                probe);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts is not null)
        {
            var probe = InferenceProviderProbeResult.Timeout(
                descriptor.ProviderId,
                descriptor.DisplayName,
                probeTimeoutMilliseconds,
                $"Probe timed out after {probeTimeoutMilliseconds} ms.");

            return new ProviderListingEntry(
                descriptor.ProviderId,
                descriptor.DisplayName,
                descriptor.Enabled,
                descriptor.SupportsStreaming,
                probe.Status,
                probe);
        }
    }

    private static JsonElement CreateStructuredContent(IReadOnlyList<ProviderListingEntry> entries, bool probed)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("count", entries.Count);
            writer.WriteBoolean("probed", probed);
            writer.WritePropertyName("providers");
            writer.WriteStartArray();

            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("providerId", entry.ProviderId);
                writer.WriteString("displayName", entry.DisplayName);
                writer.WriteBoolean("enabled", entry.Enabled);
                writer.WriteBoolean("supportsStreaming", entry.SupportsStreaming);
                writer.WriteString("status", ToStatusText(entry.Status));

                if (entry.Probe is not null)
                {
                    writer.WritePropertyName("probe");
                    writer.WriteStartObject();
                    writer.WriteString("status", ToStatusText(entry.Probe.Status));

                    if (entry.Probe.HttpStatusCode is int httpStatusCode)
                    {
                        writer.WriteNumber("httpStatusCode", httpStatusCode);
                    }
                    else
                    {
                        writer.WriteNull("httpStatusCode");
                    }

                    if (entry.Probe.ElapsedMilliseconds is int elapsedMilliseconds)
                    {
                        writer.WriteNumber("elapsedMilliseconds", elapsedMilliseconds);
                    }
                    else
                    {
                        writer.WriteNull("elapsedMilliseconds");
                    }

                    if (!string.IsNullOrWhiteSpace(entry.Probe.Message))
                    {
                        writer.WriteString("message", entry.Probe.Message);
                    }
                    else
                    {
                        writer.WriteNull("message");
                    }

                    if (!string.IsNullOrWhiteSpace(entry.Probe.Endpoint))
                    {
                        writer.WriteString("endpoint", entry.Probe.Endpoint);
                    }
                    else
                    {
                        writer.WriteNull("endpoint");
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory.ToArray());
        return document.RootElement.Clone();
    }

    private static string ToStatusText(InferenceProviderProbeStatus status)
    {
        return status switch
        {
            InferenceProviderProbeStatus.Disabled => "disabled",
            InferenceProviderProbeStatus.NotConfigured => "notConfigured",
            InferenceProviderProbeStatus.Ready => "ready",
            InferenceProviderProbeStatus.Unauthorized => "unauthorized",
            InferenceProviderProbeStatus.Unreachable => "unreachable",
            InferenceProviderProbeStatus.Timeout => "timeout",
            InferenceProviderProbeStatus.Error => "error",
            _ => "error"
        };
    }

    private static bool? TryReadOptionalBoolean(JsonElement element, ReadOnlySpan<byte> propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int? TryReadOptionalInt32(JsonElement element, ReadOnlySpan<byte> propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out intValue))
        {
            return intValue;
        }

        return null;
    }

    private sealed record ProviderListingEntry(
        string ProviderId,
        string DisplayName,
        bool Enabled,
        bool SupportsStreaming,
        InferenceProviderProbeStatus Status,
        InferenceProviderProbeResult? Probe);
}
