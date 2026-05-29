using System.Text.Json;
using System.Text;
using MCPServer.Domain.Mcp;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleOutput
{
    private const string ProviderStartHint = "Hint: start the configured provider process, then retry.";

    public static void PrintToolResult(ToolCallResult result)
    {
        Console.WriteLine(result.IsError ? "Tool returned an error result." : "Tool returned a success result.");
        WriteToolResultBody(Console.Out, result);
    }

    internal static void WriteToolResultBody(TextWriter output, ToolCallResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        foreach (var content in result.Content)
        {
            if (content is TextToolContent text)
            {
                output.WriteLine(text.Text);
            }
        }

        if (result.StructuredContent is { } structuredContent)
        {
            output.WriteLine();
            output.WriteLine("Structured content:");
            output.WriteLine(JsonSerializer.Serialize(structuredContent, McpJsonSerializerContext.Default.JsonElement));
        }

        WriteProviderStartHintIfNeeded(output, result);
    }

    public static string FormatToolResultForTranscript(ToolCallResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.IsError ? "Tool returned an error result." : "Tool returned a success result.");

        foreach (var content in result.Content)
        {
            if (content is TextToolContent text)
            {
                builder.AppendLine(text.Text);
            }
        }

        if (result.StructuredContent is { } structuredContent)
        {
            builder.AppendLine();
            builder.AppendLine("Structured content:");
            builder.AppendLine(JsonSerializer.Serialize(structuredContent, McpJsonSerializerContext.Default.JsonElement));
        }

        return builder.ToString().TrimEnd();
    }

    internal static void WriteProviderStartHintIfNeeded(TextWriter output, ToolCallResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        if (!ShouldPromptToStartProviderInference(result))
        {
            return;
        }

        output.WriteLine();
        output.WriteLine(ProviderStartHint);
    }

    private static bool ShouldPromptToStartProviderInference(ToolCallResult result)
    {
        if (result.IsError && HasConnectivityFailureText(result.Content))
        {
            return true;
        }

        return HasUnreachableProviders(result.StructuredContent);
    }

    private static bool HasConnectivityFailureText(IEnumerable<ToolContent> contents)
    {
        foreach (var content in contents)
        {
            if (content is not TextToolContent text || string.IsNullOrWhiteSpace(text.Text))
            {
                continue;
            }

            if (ContainsConnectivityFailureText(text.Text))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsConnectivityFailureText(string text)
    {
        return text.Contains("connection refused", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("no connection could be made", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("could not connect", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("failed to connect", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("not listening", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnreachableProviders(JsonElement? structuredContent)
    {
        if (structuredContent is not { } content || content.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!content.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var hasConnectivityProblem = false;
        foreach (var provider in providers.EnumerateArray())
        {
            if (provider.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!provider.TryGetProperty("status", out var statusProperty) || statusProperty.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var status = statusProperty.GetString();
            if (string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(status, "unreachable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "timeout", StringComparison.OrdinalIgnoreCase))
            {
                hasConnectivityProblem = true;
                continue;
            }

            if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase) &&
                provider.TryGetProperty("probe", out var probe) &&
                probe.ValueKind == JsonValueKind.Object &&
                probe.TryGetProperty("message", out var messageProperty) &&
                messageProperty.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(messageProperty.GetString()) &&
                ContainsConnectivityFailureText(messageProperty.GetString()!))
            {
                hasConnectivityProblem = true;
            }
        }

        return hasConnectivityProblem;
    }
}
