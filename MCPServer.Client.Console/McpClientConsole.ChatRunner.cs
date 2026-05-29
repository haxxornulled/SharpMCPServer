using System.Buffers;
using System.Text;
using System.Text.Json;
using LanguageExt;
using MCPServer.Client.Interfaces;
using MCPServer.Domain.Mcp;
using MCPServer.Inference.Abstractions.Models;

namespace MCPServer.Client.ConsoleApp;

internal static class McpClientConsoleChatRunner
{
    public static async Task<int> RunAsync(
        IMcpClientSession session,
        ConsoleOptions options,
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var initialized = await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (initialized.IsFail)
        {
            return WriteFailure(error, initialized);
        }

        var initializeResult = McpClientConsoleResultHelpers.GetValue(initialized);
        PrintConnectedMessage(output, initializeResult);

        var tools = await session.ListToolsAsync(cursor: null, cancellationToken).ConfigureAwait(false);
        if (tools.IsFail)
        {
            return WriteFailure(error, tools);
        }

        var toolList = McpClientConsoleResultHelpers.GetValue(tools);
        PrintTools(output, toolList);

        var workspaceContext = await LoadWorkspaceContextAsync(session, options, toolList.Tools, cancellationToken).ConfigureAwait(false);

        var state = ChatState.Create(options, toolList.Tools, workspaceContext);
        PrintReadyBanner(output, state);

        while (!cancellationToken.IsCancellationRequested)
        {
            PrintPrompt(output, state);

            var line = await input.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            var (handled, shouldExit) = await TryHandleCommandAsync(session, line, state, output, error, cancellationToken).ConfigureAwait(false);
            if (handled)
            {
                if (shouldExit)
                {
                    break;
                }

                continue;
            }

            if (!TryNormalizePrompt(line, out var prompt))
            {
                continue;
            }

            await SendTurnAsync(session, state, prompt, output, error, cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task SendTurnAsync(
        IMcpClientSession session,
        ChatState state,
        string prompt,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        state.AppendUserMessage(prompt);

        using var argumentsDocument = BuildGenerateArguments(state);
        var arguments = argumentsDocument.RootElement.Clone();

        var callResult = await session.CallToolAsync("inference.generate", arguments, cancellationToken).ConfigureAwait(false);
        if (callResult.IsFail)
        {
            WriteFailure(error, callResult);
            return;
        }

        var toolResult = McpClientConsoleResultHelpers.GetValue(callResult);
        if (toolResult.IsError)
        {
            PrintToolError(output, toolResult);
            return;
        }

        var responseText = ExtractAssistantText(toolResult);
        if (responseText is null)
        {
            output.WriteLine("assistant>");
            return;
        }

        var assistantMetadata = ExtractAssistantMetadata(toolResult);
        PrintAssistantMessage(output, responseText, assistantMetadata);
        state.AppendAssistantMessage(responseText);
    }

    private static JsonDocument BuildGenerateArguments(ChatState state)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            if (!string.IsNullOrWhiteSpace(state.ProviderId))
            {
                writer.WriteString("providerId", state.ProviderId);
            }

            if (!string.IsNullOrWhiteSpace(state.Model))
            {
                writer.WriteString("model", state.Model);
            }

            if (state.RoutingStrategy is { } routingStrategy)
            {
                writer.WriteString("strategy", routingStrategy.ToString());
            }

            if (state.FallbackProviderIds.Count > 0)
            {
                writer.WritePropertyName("fallbackProviderIds");
                writer.WriteStartArray();
                foreach (var fallbackProviderId in state.FallbackProviderIds)
                {
                    writer.WriteStringValue(fallbackProviderId);
                }

                writer.WriteEndArray();
            }

            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            foreach (var message in state.Messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", GetRoleName(message.Role));
                writer.WriteString("content", message.Content);

                if (!string.IsNullOrWhiteSpace(message.Name))
                {
                    writer.WriteString("name", message.Name);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(buffer.WrittenMemory.ToArray());
    }

    private static async Task<(bool Handled, bool ShouldExit)> TryHandleCommandAsync(
        IMcpClientSession session,
        string line,
        ChatState state,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var command = line.Trim();
        if (!command.StartsWith('/'))
        {
            return (false, false);
        }

        if (command.StartsWith("//", StringComparison.Ordinal))
        {
            return (false, false);
        }

        if (command.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp(output);
            return (true, false);
        }

        if (command.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("/quit", StringComparison.OrdinalIgnoreCase))
        {
            return (true, true);
        }

        if (command.Equals("/reset", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            state.ResetConversation();
            output.WriteLine("Transcript reset.");
            return (true, false);
        }

        if (TryHandleSetterCommand(command, "/provider", state.SetProvider, output, error, "provider"))
        {
            return (true, false);
        }

        if (TryHandleSetterCommand(command, "/model", state.SetModel, output, error, "model"))
        {
            return (true, false);
        }

        if (TryHandleSystemCommand(command, state, output, error))
        {
            return (true, false);
        }

        if (TryHandleStrategyCommand(command, state, output, error))
        {
            return (true, false);
        }

        if (TryHandleFallbackCommand(command, state, output, error))
        {
            return (true, false);
        }

        if (TryHandlePromptCommand(command, state, output))
        {
            return (true, false);
        }

        if (TryHandleToolsCommand(command, state, output))
        {
            return (true, false);
        }

        if (await TryHandleWorkspaceSearchCommandAsync(session, command, state, output, error, cancellationToken).ConfigureAwait(false))
        {
            return (true, false);
        }

        if (await TryHandleWorkspaceFileCommandAsync(session, command, state, output, error, cancellationToken).ConfigureAwait(false))
        {
            return (true, false);
        }

        if (await TryHandleToolCommandAsync(session, command, state, output, error, cancellationToken).ConfigureAwait(false))
        {
            return (true, false);
        }

        if (await TryHandleCompactCommandAsync(session, command, state, output, error, cancellationToken).ConfigureAwait(false))
        {
            return (true, false);
        }

        error.WriteLine($"Unknown chat command: {command}");
        error.WriteLine("Type /help for available commands.");
        return (true, false);
    }

    private static bool TryHandleSetterCommand(
        string command,
        string commandName,
        Action<string?> setter,
        TextWriter output,
        TextWriter error,
        string label)
    {
        if (!command.Equals(commandName, StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith(commandName + " ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = command[commandName.Length..].Trim();
        if (value.Length == 0)
        {
            error.WriteLine($"{commandName} requires a value or 'clear'.");
            return true;
        }

        if (string.Equals(value, "clear", StringComparison.OrdinalIgnoreCase))
        {
            setter(null);
            output.WriteLine($"{label} cleared.");
            return true;
        }

        setter(value);
        output.WriteLine($"{label} set to {value}.");
        return true;
    }

    private static bool TryHandleSystemCommand(
        string command,
        ChatState state,
        TextWriter output,
        TextWriter error)
    {
        if (!command.Equals("/system", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/system ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = command["/system".Length..].Trim();
        if (value.Length == 0 || string.Equals(value, "clear", StringComparison.OrdinalIgnoreCase))
        {
            state.SetSystemPrompt(null);
            output.WriteLine("System prompt cleared.");
            return true;
        }

        state.SetSystemPrompt(value);
        output.WriteLine("System prompt updated and transcript reset.");
        return true;
    }

    private static bool TryHandleStrategyCommand(
        string command,
        ChatState state,
        TextWriter output,
        TextWriter error)
    {
        if (!command.Equals("/strategy", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/strategy ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = command["/strategy".Length..].Trim();
        if (value.Length == 0 || string.Equals(value, "clear", StringComparison.OrdinalIgnoreCase))
        {
            state.SetRoutingStrategy(null);
            output.WriteLine("Routing strategy cleared.");
            return true;
        }

        if (!TryParseRoutingStrategy(value, out var strategy))
        {
            error.WriteLine($"Unsupported routing strategy: {value}");
            error.WriteLine("Valid values: PrimaryOnly, PrimaryThenFallback, FanOutCompare, TandemValidate, SecondOpinion.");
            return true;
        }

        state.SetRoutingStrategy(strategy);
        output.WriteLine($"Routing strategy set to {strategy}.");
        return true;
    }

    private static bool TryHandleFallbackCommand(
        string command,
        ChatState state,
        TextWriter output,
        TextWriter error)
    {
        if (!command.Equals("/fallback", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/fallback ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = command["/fallback".Length..].Trim();
        if (value.Length == 0 || string.Equals(value, "clear", StringComparison.OrdinalIgnoreCase))
        {
            state.SetFallbackProviderIds([]);
            output.WriteLine("Fallback providers cleared.");
            return true;
        }

        var providerIds = ParseProviderIds(value);
        if (providerIds.Count == 0)
        {
            error.WriteLine("Fallback providers requires at least one provider id or 'clear'.");
            return true;
        }

        state.SetFallbackProviderIds(providerIds);
        output.WriteLine($"Fallback providers set to {string.Join(", ", providerIds)}.");
        return true;
    }

    private static bool TryHandlePromptCommand(
        string command,
        ChatState state,
        TextWriter output)
    {
        if (!command.Equals("/prompt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        PrintTranscript(output, state);
        return true;
    }

    private static bool TryHandleToolsCommand(
        string command,
        ChatState state,
        TextWriter output)
    {
        if (!command.Equals("/tools", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/tools ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var filter = command["/tools".Length..].Trim();
        var tools = state.AvailableTools;
        if (tools.Count == 0)
        {
            output.WriteLine("No tools are currently available.");
            return true;
        }

        output.WriteLine(filter.Length == 0 ? "Tools exposed by server:" : $"Tools exposed by server matching '{filter}':");
        foreach (var tool in tools)
        {
            if (!string.IsNullOrWhiteSpace(filter) &&
                !tool.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(tool.Description) &&
                !tool.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            output.WriteLine($"- {tool.Name}: {tool.Description}");
        }

        output.WriteLine();
        return true;
    }

    private static async Task<bool> TryHandleWorkspaceFileCommandAsync(
        IMcpClientSession session,
        string command,
        ChatState state,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var separatorIndex = command.IndexOfAny([' ', '\t']);
        var commandName = separatorIndex < 0 ? command : command[..separatorIndex];
        if (!command.Equals("/read", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/read ", StringComparison.OrdinalIgnoreCase) &&
            !command.Equals("/write", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/write ", StringComparison.OrdinalIgnoreCase) &&
            !command.Equals("/patch", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/patch ", StringComparison.OrdinalIgnoreCase) &&
            !command.Equals("/edit", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/edit ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = command[commandName.Length..].Trim();
        if (value.Length == 0)
        {
            error.WriteLine($"{commandName} requires a JSON object payload. Use /tool for arbitrary tool calls.");
            return true;
        }

        var toolName = commandName.Equals("/read", StringComparison.OrdinalIgnoreCase)
            ? "workspace.files.read"
            : commandName.Equals("/write", StringComparison.OrdinalIgnoreCase)
                ? "workspace.files.write"
                : "workspace.files.applyPatch";

        JsonElement? arguments = null;
        using var argumentsDocument = McpClientConsoleArgumentParser.ParseArguments(value, out arguments, out var parseError);
        if (!string.IsNullOrEmpty(parseError))
        {
            error.WriteLine(parseError);
            return true;
        }

        return await TryCallToolAsync(session, state, toolName, arguments, output, error, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TryHandleWorkspaceSearchCommandAsync(
        IMcpClientSession session,
        string command,
        ChatState state,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!command.Equals("/search", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/search ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = command["/search".Length..].Trim();
        if (value.Length == 0)
        {
            error.WriteLine("/search requires a JSON object payload.");
            return true;
        }

        JsonElement? arguments = null;
        using var argumentsDocument = McpClientConsoleArgumentParser.ParseArguments(value, out arguments, out var parseError);
        if (!string.IsNullOrEmpty(parseError))
        {
            error.WriteLine(parseError);
            return true;
        }

        return await TryCallToolAsync(session, state, "workspace.files.search", arguments, output, error, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TryHandleToolCommandAsync(
        IMcpClientSession session,
        string command,
        ChatState state,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!command.Equals("/tool", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/tool ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = command["/tool".Length..].Trim();
        if (value.Length == 0)
        {
            error.WriteLine("/tool requires a tool name.");
            return true;
        }

        var separatorIndex = value.IndexOfAny([' ', '\t']);
        string toolName;
        string? argumentsJson = null;
        if (separatorIndex < 0)
        {
            toolName = value;
        }
        else
        {
            toolName = value[..separatorIndex].Trim();
            argumentsJson = value[separatorIndex..].Trim();
        }

        if (string.IsNullOrWhiteSpace(toolName))
        {
            error.WriteLine("/tool requires a tool name.");
            return true;
        }

        JsonElement? arguments = null;
        using var argumentsDocument = McpClientConsoleArgumentParser.ParseArguments(argumentsJson, out arguments, out var parseError);
        if (!string.IsNullOrEmpty(parseError))
        {
            error.WriteLine(parseError);
            return true;
        }

        return await TryCallToolAsync(session, state, toolName, arguments, output, error, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TryHandleCompactCommandAsync(
        IMcpClientSession session,
        string command,
        ChatState state,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!command.Equals("/compact", StringComparison.OrdinalIgnoreCase) &&
            !command.StartsWith("/compact ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var instructions = command["/compact".Length..].Trim();
        output.WriteLine("Compacting transcript...");

        using var argumentsDocument = BuildCompactArguments(state, instructions);
        var arguments = argumentsDocument.RootElement.Clone();

        var callResult = await session.CallToolAsync("inference.generate", arguments, cancellationToken).ConfigureAwait(false);
        if (callResult.IsFail)
        {
            WriteFailure(error, callResult);
            return true;
        }

        var toolResult = McpClientConsoleResultHelpers.GetValue(callResult);
        if (toolResult.IsError)
        {
            PrintToolError(output, toolResult);
            return true;
        }

        var summary = ExtractAssistantText(toolResult);
        if (summary is null)
        {
            error.WriteLine("Transcript compaction did not return a summary.");
            return true;
        }

        state.SetConversationSummary(summary);
        output.WriteLine("Transcript compacted.");
        return true;
    }

    private static async Task<bool> TryCallToolAsync(
        IMcpClientSession session,
        ChatState state,
        string toolName,
        JsonElement? arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        output.WriteLine($"Calling tool: {toolName}");

        var callResult = await session.CallToolAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);
        if (callResult.IsFail)
        {
            WriteFailure(error, callResult);
            return true;
        }

        var toolResult = McpClientConsoleResultHelpers.GetValue(callResult);
        McpClientConsoleOutput.PrintToolResult(toolResult);
        state.AppendToolMessage(toolName, McpClientConsoleOutput.FormatToolResultForTranscript(toolResult));
        return true;
    }

    private static bool TryNormalizePrompt(string line, out string prompt)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            prompt = string.Empty;
            return false;
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            prompt = trimmed[1..];
            return true;
        }

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            prompt = string.Empty;
            return false;
        }

        prompt = trimmed;
        return true;
    }

    private static void PrintConnectedMessage(TextWriter output, InitializeResult initializeResult)
    {
        output.WriteLine($"Connected to {initializeResult.ServerInfo.Name} {initializeResult.ServerInfo.Version} using MCP {initializeResult.ProtocolVersion}.");
    }

    private static void PrintTools(TextWriter output, ToolsListResult tools)
    {
        output.WriteLine("Tools exposed by server:");
        foreach (var tool in tools.Tools)
        {
            output.WriteLine($"- {tool.Name}: {tool.Description}");
        }
    }

    private static void PrintReadyBanner(TextWriter output, ChatState state)
    {
        output.WriteLine("Chat mode ready.");
        output.WriteLine("Type /help for commands. /prompt shows the transcript, /tools lists available tools, /tool calls any MCP tool, /read/write/edit touch workspace files, /compact compresses context, and /exit quits. Prefix a prompt with // to send a literal slash.");
        output.WriteLine($"Route: {state.PromptSummary}");
        if (state.HasWorkspaceContext)
        {
            output.WriteLine($"Workspace: {state.WorkspaceBanner}");
        }

        if (state.HasSystemPrompt)
        {
            output.WriteLine("System prompt: set");
        }

        output.WriteLine();
    }

    private static void PrintPrompt(TextWriter output, ChatState state)
    {
        output.Write($"> [{state.PromptSummary}] ");
        output.Flush();
    }

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine("Commands:");
        output.WriteLine("  /help");
        output.WriteLine("  /exit or /quit");
        output.WriteLine("  /reset or /clear");
        output.WriteLine("  /prompt");
        output.WriteLine("  /tools [filter]");
        output.WriteLine("  /tool <name> [json]");
        output.WriteLine("  /read <json>");
        output.WriteLine("  /write <json>");
        output.WriteLine("  /patch <json with message>");
        output.WriteLine("  /edit <json with message>");
        output.WriteLine("  /compact [instructions]");
        output.WriteLine("  /provider <id> | /provider clear");
        output.WriteLine("  /model <name> | /model clear");
        output.WriteLine("  /system <prompt> | /system clear");
        output.WriteLine("  /strategy <PrimaryOnly|PrimaryThenFallback|FanOutCompare|TandemValidate|SecondOpinion> | /strategy clear");
        output.WriteLine("  /fallback <id[,id...]> | /fallback clear");
        output.WriteLine();
        output.WriteLine("Type a message to send it through inference.generate.");
    }

    private static void PrintToolError(TextWriter output, ToolCallResult result)
    {
        output.WriteLine("assistant> [tool returned an error]");
        foreach (var content in result.Content)
        {
            if (content is TextToolContent text)
            {
                output.WriteLine(text.Text);
            }
        }

        McpClientConsoleOutput.WriteProviderStartHintIfNeeded(output, result);
    }

    private static void PrintAssistantMessage(TextWriter output, string text, AssistantMetadata metadata)
    {
        output.WriteLine("assistant>");

        var lines = text.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            output.WriteLine($"  {line}");
        }

        var statusLine = BuildStatusLine(metadata);
        if (!string.IsNullOrWhiteSpace(statusLine))
        {
            output.WriteLine($"  [{statusLine}]");
        }

        var chainLine = BuildChainLine(metadata);
        if (!string.IsNullOrWhiteSpace(chainLine))
        {
            output.WriteLine($"  [{chainLine}]");
        }

        output.WriteLine();
    }

    private static void PrintTranscript(TextWriter output, ChatState state)
    {
        output.WriteLine(CreateTranscriptDump(state));
        output.WriteLine();
    }

    private static JsonDocument BuildCompactArguments(ChatState state, string instructions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            if (!string.IsNullOrWhiteSpace(state.ProviderId))
            {
                writer.WriteString("providerId", state.ProviderId);
            }

            if (!string.IsNullOrWhiteSpace(state.Model))
            {
                writer.WriteString("model", state.Model);
            }

            if (state.RoutingStrategy is { } routingStrategy)
            {
                writer.WriteString("strategy", routingStrategy.ToString());
            }

            if (state.FallbackProviderIds.Count > 0)
            {
                writer.WritePropertyName("fallbackProviderIds");
                writer.WriteStartArray();
                foreach (var fallbackProviderId in state.FallbackProviderIds)
                {
                    writer.WriteStringValue(fallbackProviderId);
                }

                writer.WriteEndArray();
            }

            writer.WriteString("systemPrompt", "You compress chat transcripts for later continuation. Preserve decisions, unresolved tasks, tool outputs, file paths, provider or model choices, routing strategy, fallback providers, and user preferences. Return only the compacted memory summary.");
            writer.WriteString("prompt", BuildCompactPrompt(state, instructions));
            writer.WriteNumber("maxTokens", 512);
            writer.WriteNumber("temperature", 0.1);

            writer.WriteEndObject();
        }

        return JsonDocument.Parse(buffer.WrittenMemory.ToArray());
    }

    private static string BuildCompactPrompt(ChatState state, string instructions)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Summarize the following chat transcript for future continuation.");
        builder.AppendLine("Keep it concise, actionable, and focused on durable context.");

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            builder.AppendLine();
            builder.AppendLine("Additional instructions:");
            builder.AppendLine(instructions.Trim());
        }

        builder.AppendLine();
        builder.AppendLine(CreateTranscriptDump(state));
        return builder.ToString();
    }

    private static string CreateTranscriptDump(ChatState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Transcript state:");
        builder.AppendLine($"  provider={state.ProviderLabel}");
        builder.AppendLine($"  model={state.ModelLabel}");
        builder.AppendLine($"  strategy={state.RoutingStrategyLabel}");
        builder.AppendLine($"  fallback={state.FallbackLabel}");
        builder.AppendLine($"  summary={state.SummaryLabel}");
        builder.AppendLine($"  messages={state.Messages.Count}");

        for (var i = 0; i < state.Messages.Count; i++)
        {
            var message = state.Messages[i];
            builder.Append("  [");
            builder.Append(i);
            builder.Append("] ");
            builder.Append(GetMessageLabel(message));
            builder.Append(": ");
            builder.AppendLine(message.Content);
        }

        return builder.ToString().TrimEnd();
    }

    private static string? ExtractAssistantText(ToolCallResult result)
    {
        var builder = new StringBuilder();
        foreach (var content in result.Content)
        {
            if (content is not TextToolContent text)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(text.Text);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static AssistantMetadata ExtractAssistantMetadata(ToolCallResult result)
    {
        if (result.StructuredContent is not { } structuredContent || structuredContent.ValueKind != JsonValueKind.Object)
        {
            return AssistantMetadata.Empty;
        }

        var providerId = TryGetString(structuredContent, "providerId");
        var model = TryGetString(structuredContent, "model");
        var finishReason = TryGetString(structuredContent, "finishReason");
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (structuredContent.TryGetProperty("metadata", out var metadataElement) &&
            metadataElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in metadataElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                metadata[property.Name] = value;
            }
        }

        return new AssistantMetadata(providerId, model, finishReason, metadata);
    }

    private static string? BuildStatusLine(AssistantMetadata metadata)
    {
        var fragments = new List<string>();

        if (!string.IsNullOrWhiteSpace(metadata.ProviderId))
        {
            fragments.Add($"provider={metadata.ProviderId}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Model))
        {
            fragments.Add($"model={metadata.Model}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.FinishReason))
        {
            fragments.Add($"finish={metadata.FinishReason}");
        }

        AppendMetricFragments(fragments, metadata.Metadata, string.Empty);
        return fragments.Count == 0 ? null : string.Join(", ", fragments);
    }

    private static string? BuildChainLine(AssistantMetadata metadata)
    {
        if (TryGetString(metadata.Metadata, "secondOpinion.status") is { } secondOpinionStatus)
        {
            var fragments = new List<string>
            {
                $"secondOpinion status={secondOpinionStatus}"
            };

            AppendModelFragment(
                fragments,
                "primary",
                TryGetString(metadata.Metadata, "secondOpinion.primaryProviderId"),
                TryGetString(metadata.Metadata, "secondOpinion.primaryModel"),
                metadata.Metadata,
                "secondOpinion.primary.",
                includeMetrics: true);

            if (TryGetString(metadata.Metadata, "secondOpinion.reviewerProviderId") is { } reviewerProviderId &&
                TryGetString(metadata.Metadata, "secondOpinion.reviewerModel") is { } reviewerModel)
            {
                AppendModelFragment(
                    fragments,
                    "reviewer",
                    reviewerProviderId,
                    reviewerModel,
                    metadata.Metadata,
                    "secondOpinion.reviewer.",
                    includeMetrics: false);
            }
            else if (TryGetString(metadata.Metadata, "secondOpinion.reviewerAttemptedProviderId") is { } reviewerAttemptedProviderId)
            {
                fragments.Add($"reviewerAttempted={reviewerAttemptedProviderId}");
            }

            if (TryGetString(metadata.Metadata, "secondOpinion.reviewError") is { } reviewError)
            {
                fragments.Add($"reviewError={reviewError}");
            }

            return fragments.Count > 1 ? string.Join(", ", fragments) : null;
        }

        if (TryGetString(metadata.Metadata, "tandem.status") is { } tandemStatus)
        {
            var fragments = new List<string>
            {
                $"tandem status={tandemStatus}"
            };

            AppendModelFragment(
                fragments,
                "primary",
                TryGetString(metadata.Metadata, "tandem.primaryProviderId"),
                TryGetString(metadata.Metadata, "tandem.primaryModel"),
                metadata.Metadata,
                "tandem.primary.",
                includeMetrics: true);

            if (TryGetString(metadata.Metadata, "tandem.secondaryProviderId") is { } secondaryProviderId &&
                TryGetString(metadata.Metadata, "tandem.secondaryModel") is { } secondaryModel)
            {
                AppendModelFragment(
                    fragments,
                    "secondary",
                    secondaryProviderId,
                    secondaryModel,
                    metadata.Metadata,
                    "tandem.secondary.",
                    includeMetrics: true);
            }

            if (TryGetString(metadata.Metadata, "tandem.validatorProviderId") is { } validatorProviderId)
            {
                var validatorModel = TryGetString(metadata.Metadata, "tandem.validatorModel");
                if (!string.IsNullOrWhiteSpace(validatorModel))
                {
                    fragments.Add($"validator={validatorProviderId}/{validatorModel}");
                }
                else
                {
                    fragments.Add($"validator={validatorProviderId}");
                }
            }

            if (TryGetString(metadata.Metadata, "tandem.validatorProviderId") is null &&
                TryGetString(metadata.Metadata, "tandem.validatorModel") is { } attemptedValidatorModel)
            {
                fragments.Add($"validatorModel={attemptedValidatorModel}");
            }

            return fragments.Count > 1 ? string.Join(", ", fragments) : null;
        }

        return null;
    }

    private static void AppendModelFragment(
        ICollection<string> fragments,
        string label,
        string? providerId,
        string? model,
        IReadOnlyDictionary<string, string> metadata,
        string metricPrefix,
        bool includeMetrics)
    {
        var descriptor = BuildProviderModelDescriptor(providerId, model);
        if (includeMetrics)
        {
            var metrics = BuildMetricFragment(metadata, metricPrefix);
            if (!string.IsNullOrWhiteSpace(metrics))
            {
                descriptor = $"{descriptor} ({metrics})";
            }
        }

        fragments.Add($"{label}={descriptor}");
    }

    private static string BuildProviderModelDescriptor(string? providerId, string? model)
    {
        if (string.IsNullOrWhiteSpace(providerId) && string.IsNullOrWhiteSpace(model))
        {
            return "unknown";
        }

        if (string.IsNullOrWhiteSpace(providerId))
        {
            return model!.Trim();
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return providerId.Trim();
        }

        return $"{providerId.Trim()}/{model.Trim()}";
    }

    private static string? BuildMetricFragment(
        IReadOnlyDictionary<string, string> metadata,
        string prefix)
    {
        var fragments = new List<string>();
        AppendMetricFragments(fragments, metadata, prefix);
        return fragments.Count == 0 ? null : string.Join(", ", fragments);
    }

    private static void AppendMetricFragments(
        ICollection<string> fragments,
        IReadOnlyDictionary<string, string> metadata,
        string prefix)
    {
        AppendMetricFragment(fragments, metadata, prefix + "generationElapsedMilliseconds", "elapsed", suffix: "ms");
        AppendMetricFragment(fragments, metadata, prefix + "loadDurationMilliseconds", "load", suffix: "ms");
        AppendMetricFragment(fragments, metadata, prefix + "tokensPerSecond", "tps");
        AppendMetricFragment(fragments, metadata, prefix + "inputTokensPerSecond", "inputTps");
        AppendMetricFragment(fragments, metadata, prefix + "outputTokensPerSecond", "outputTps");
    }

    private static void AppendMetricFragment(
        ICollection<string> fragments,
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string label,
        string? suffix = null)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        fragments.Add(string.IsNullOrWhiteSpace(suffix) ? $"{label}={value}" : $"{label}={value}{suffix}");
    }

    private static bool TryParseRoutingStrategy(string value, out InferenceRoutingStrategy strategy)
    {
        var normalized = NormalizeCommandToken(value);
        if (Enum.TryParse<InferenceRoutingStrategy>(normalized, ignoreCase: true, out strategy))
        {
            return true;
        }

        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static string GetRoleName(InferenceRole role)
    {
        return role switch
        {
            InferenceRole.System => "system",
            InferenceRole.User => "user",
            InferenceRole.Assistant => "assistant",
            InferenceRole.Tool => "tool",
            _ => "user"
        };
    }

    private static string GetMessageLabel(InferenceMessage message)
    {
        var roleName = GetRoleName(message.Role);
        return string.IsNullOrWhiteSpace(message.Name) ? roleName : $"{roleName}({message.Name})";
    }

    private static string NormalizeCommandToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static List<string> ParseProviderIds(string value)
    {
        var tokens = value.Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return [];
        }

        var providerIds = new List<string>(tokens.Length);
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var providerId = token.Trim();
            if (seen.Add(providerId))
            {
                providerIds.Add(providerId);
            }
        }

        return providerIds;
    }

    private static int WriteFailure<T>(TextWriter error, Fin<T> value)
    {
        var message = McpClientConsoleResultHelpers.GetError(value);
        error.WriteLine(message);
        McpClientConsoleOutput.WriteProviderStartHintIfNeeded(error, message);
        return 1;
    }

    private static async Task<WorkspaceContext> LoadWorkspaceContextAsync(
        IMcpClientSession session,
        ConsoleOptions options,
        IReadOnlyList<McpToolDescriptor> availableTools,
        CancellationToken cancellationToken)
    {
        var checkoutRoot = McpClientConsoleWorkspaceContextResolver.ResolveCheckoutRoot(options);
        var serverWorkingDirectory = NormalizePathForDisplay(options.WorkingDirectory);
        IReadOnlyList<WorkspaceRootSnapshot> rootSnapshots = [];
        var status = string.Empty;

        if (availableTools.Any(tool => string.Equals(tool.Name, "workspace.roots.list", StringComparison.OrdinalIgnoreCase)))
        {
            var rootsResult = await session.CallToolAsync("workspace.roots.list", null, cancellationToken).ConfigureAwait(false);
            if (rootsResult.IsFail)
            {
                status = "workspace.roots.list could not be loaded.";
            }
            else
            {
                var rootsToolResult = McpClientConsoleResultHelpers.GetValue(rootsResult);
                if (rootsToolResult.IsError)
                {
                    status = "workspace.roots.list returned an error.";
                }
                else if (!TryReadWorkspaceRoots(rootsToolResult.StructuredContent, out var snapshots))
                {
                    status = "workspace.roots.list returned no structured root data.";
                }
                else
                {
                    rootSnapshots = snapshots;
                }
            }
        }
        else
        {
            status = "workspace.roots.list is not available from this session.";
        }

        var primaryWorkspaceRoot = SelectPrimaryWorkspaceRoot(rootSnapshots, checkoutRoot, serverWorkingDirectory);
        return new WorkspaceContext(checkoutRoot, serverWorkingDirectory, primaryWorkspaceRoot, rootSnapshots, status);
    }

    private static bool TryReadWorkspaceRoots(JsonElement? structuredContent, out IReadOnlyList<WorkspaceRootSnapshot> roots)
    {
        roots = [];

        if (structuredContent is not { ValueKind: JsonValueKind.Object } rootObject)
        {
            return false;
        }

        if (!rootObject.TryGetProperty("roots", out var rootsProperty) || rootsProperty.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var snapshots = new List<WorkspaceRootSnapshot>();
        foreach (var rootElement in rootsProperty.EnumerateArray())
        {
            if (rootElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            snapshots.Add(new WorkspaceRootSnapshot(
                TryReadOptionalString(rootElement, "name") ?? "workspace",
                TryReadOptionalString(rootElement, "path") ?? string.Empty,
                TryReadOptionalString(rootElement, "kind") ?? "workspace",
                TryGetBoolean(rootElement, "allowWrite"),
                TryGetBoolean(rootElement, "exists"),
                TryReadOptionalString(rootElement, "sourceRootName")));
        }

        roots = snapshots;
        return true;
    }

    private static string NormalizePathForDisplay(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFullPath(path.Trim());
    }

    internal static string ResolveCheckoutRoot(string startDirectory)
    {
        return McpClientConsoleWorkspaceContextResolver.ResolveCheckoutRoot(startDirectory);
    }

    private static WorkspaceRootSnapshot? SelectPrimaryWorkspaceRoot(
        IReadOnlyList<WorkspaceRootSnapshot> roots,
        string checkoutRoot,
        string? serverWorkingDirectory)
    {
        if (roots.Count == 0)
        {
            return null;
        }

        var checkoutMatch = FindMatchingRoot(roots, checkoutRoot);
        if (checkoutMatch is not null)
        {
            return checkoutMatch;
        }

        var serverWorkingDirectoryMatch = FindMatchingRoot(roots, serverWorkingDirectory);
        if (serverWorkingDirectoryMatch is not null)
        {
            return serverWorkingDirectoryMatch;
        }

        var namedWorkspaceMatch = roots.FirstOrDefault(root => string.Equals(root.Name, "workspace", StringComparison.OrdinalIgnoreCase));
        if (namedWorkspaceMatch is not null)
        {
            return namedWorkspaceMatch;
        }

        var writableWorkspaceMatch = roots.FirstOrDefault(root =>
            string.Equals(root.Kind, "workspace", StringComparison.OrdinalIgnoreCase) &&
            root.AllowWrite);
        if (writableWorkspaceMatch is not null)
        {
            return writableWorkspaceMatch;
        }

        return roots[0];
    }

    private static WorkspaceRootSnapshot? FindMatchingRoot(IReadOnlyList<WorkspaceRootSnapshot> roots, string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return null;
        }

        var normalizedCandidatePath = Path.TrimEndingDirectorySeparator(NormalizePathForDisplay(candidatePath));
        return roots.FirstOrDefault(root =>
            string.Equals(
                Path.TrimEndingDirectorySeparator(root.Path),
                normalizedCandidatePath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    }

    private static string? TryReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static string? TryGetString(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private sealed record AssistantMetadata(
        string? ProviderId,
        string? Model,
        string? FinishReason,
        IReadOnlyDictionary<string, string> Metadata)
    {
        public static readonly AssistantMetadata Empty = new(null, null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;
    }

    private sealed class ChatState
    {
        private readonly List<InferenceMessage> _messages = [];

        private ChatState(IReadOnlyList<McpToolDescriptor> availableTools, WorkspaceContext? workspaceContext)
        {
            AvailableTools = availableTools;
            WorkspaceContext = workspaceContext;
        }

        public string? ProviderId { get; private set; }

        public string? Model { get; private set; }

        public string? SystemPrompt { get; private set; }

        public string? ConversationSummary { get; private set; }

        public WorkspaceContext? WorkspaceContext { get; private set; }

        public InferenceRoutingStrategy? RoutingStrategy { get; private set; }

        public IReadOnlyList<string> FallbackProviderIds => _fallbackProviderIds;

        private readonly List<string> _fallbackProviderIds = [];

        public IReadOnlyList<McpToolDescriptor> AvailableTools { get; }

        public bool HasSystemPrompt => !string.IsNullOrWhiteSpace(SystemPrompt);

        public bool HasWorkspaceContext => WorkspaceContext is not null;

        public string ProviderLabel => string.IsNullOrWhiteSpace(ProviderId) ? "router default" : ProviderId;

        public string ModelLabel => string.IsNullOrWhiteSpace(Model) ? "provider default" : Model;

        public string RoutingStrategyLabel => RoutingStrategy?.ToString() ?? "PrimaryThenFallback";

        public string FallbackLabel => _fallbackProviderIds.Count == 0 ? "router default" : string.Join(", ", _fallbackProviderIds);

        public string SummaryLabel => string.IsNullOrWhiteSpace(ConversationSummary) ? "none" : "present";

        public string WorkspaceLabel => WorkspaceContext is null ? "none" : "loaded";

        public string WorkspaceBanner => WorkspaceContext?.BannerSummary ?? "unavailable";

        public string PromptSummary => $"provider={ProviderLabel}, model={ModelLabel}, strategy={RoutingStrategyLabel}, fallback={FallbackLabel}, workspace={WorkspaceLabel}, summary={SummaryLabel}";

        public IReadOnlyList<InferenceMessage> Messages => _messages;

        public static ChatState Create(ConsoleOptions options, IReadOnlyList<McpToolDescriptor> availableTools, WorkspaceContext? workspaceContext)
        {
            var state = new ChatState(availableTools, workspaceContext)
            {
                ProviderId = NormalizeOptionalString(options.InferenceProviderId),
                Model = NormalizeOptionalString(options.InferenceModel),
                SystemPrompt = NormalizeOptionalString(options.InferenceSystemPrompt)
            };

            state.ResetConversation();
            return state;
        }

        public void SetProvider(string? providerId)
        {
            ProviderId = NormalizeOptionalString(providerId);
            ResetConversation();
        }

        public void SetModel(string? model)
        {
            Model = NormalizeOptionalString(model);
            ResetConversation();
        }

        public void SetSystemPrompt(string? systemPrompt)
        {
            SystemPrompt = NormalizeOptionalString(systemPrompt);
            ResetConversation();
        }

        public void SetConversationSummary(string? summary)
        {
            ConversationSummary = NormalizeOptionalString(summary);
            ResetConversation();
        }

        public void SetRoutingStrategy(InferenceRoutingStrategy? routingStrategy)
        {
            RoutingStrategy = routingStrategy;
        }

        public void SetFallbackProviderIds(IEnumerable<string>? providerIds)
        {
            _fallbackProviderIds.Clear();

            if (providerIds is null)
            {
                return;
            }

            foreach (var providerId in providerIds)
            {
                if (string.IsNullOrWhiteSpace(providerId))
                {
                    continue;
                }

                var trimmed = providerId.Trim();
                if (_fallbackProviderIds.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _fallbackProviderIds.Add(trimmed);
            }
        }

        public void ResetConversation()
        {
            _messages.Clear();

            if (WorkspaceContext is not null)
            {
                _messages.Add(new InferenceMessage(InferenceRole.System, WorkspaceContext.SystemPrompt, "workspace-context"));
            }

            if (!string.IsNullOrWhiteSpace(SystemPrompt))
            {
                _messages.Add(new InferenceMessage(InferenceRole.System, SystemPrompt));
            }

            if (!string.IsNullOrWhiteSpace(ConversationSummary))
            {
                _messages.Add(new InferenceMessage(InferenceRole.System, ConversationSummary, "conversation-summary"));
            }
        }

        public void AppendUserMessage(string content)
        {
            _messages.Add(new InferenceMessage(InferenceRole.User, content));
        }

        public void AppendAssistantMessage(string content)
        {
            _messages.Add(new InferenceMessage(InferenceRole.Assistant, content));
        }

        public void AppendToolMessage(string toolName, string content)
        {
            _messages.Add(new InferenceMessage(InferenceRole.Tool, content, NormalizeOptionalString(toolName)));
        }

        private static string? NormalizeOptionalString(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    private sealed record WorkspaceContext(
        string CheckoutRoot,
        string ServerWorkingDirectory,
        WorkspaceRootSnapshot? PrimaryWorkspaceRoot,
        IReadOnlyList<WorkspaceRootSnapshot> Roots,
        string Status)
    {
        public string SystemPrompt => BuildSystemPrompt();

        public string BannerSummary => BuildBannerSummary();

        private string BuildSystemPrompt()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Workspace context:");
            builder.AppendLine($"  checkout root: {CheckoutRoot}");

            if (!string.IsNullOrWhiteSpace(ServerWorkingDirectory))
            {
                builder.AppendLine($"  server working directory: {ServerWorkingDirectory}");
            }

            if (PrimaryWorkspaceRoot is not null)
            {
                builder.Append("  active workspace root: ");
                builder.Append(PrimaryWorkspaceRoot.Name);
                builder.Append(": ");
                builder.Append(PrimaryWorkspaceRoot.Path);
                builder.Append(" (");
                builder.Append(PrimaryWorkspaceRoot.Kind);
                builder.Append(PrimaryWorkspaceRoot.AllowWrite ? ", writable" : ", read-only");
                builder.Append(PrimaryWorkspaceRoot.Exists ? ", exists" : ", missing");
                if (!string.IsNullOrWhiteSpace(PrimaryWorkspaceRoot.SourceRootName))
                {
                    builder.Append(", source=");
                    builder.Append(PrimaryWorkspaceRoot.SourceRootName);
                }

                builder.AppendLine(")");
            }

            if (Roots.Count == 0)
            {
                builder.AppendLine("  workspace roots: unavailable");
            }
            else
            {
                builder.AppendLine("  workspace roots:");
                foreach (var root in Roots)
                {
                    builder.Append("    - ");
                    builder.Append(root.Name);
                    builder.Append(": ");
                    builder.Append(root.Path);
                    builder.Append(" (");
                    builder.Append(root.Kind);
                    builder.Append(root.AllowWrite ? ", writable" : ", read-only");
                    builder.Append(root.Exists ? ", exists" : ", missing");
                    if (!string.IsNullOrWhiteSpace(root.SourceRootName))
                    {
                        builder.Append(", source=");
                        builder.Append(root.SourceRootName);
                    }

                    builder.AppendLine(")");
                }
            }

            if (!string.IsNullOrWhiteSpace(Status))
            {
                builder.AppendLine($"  note: {Status}");
            }

            return builder.ToString().TrimEnd();
        }

        private string BuildBannerSummary()
        {
            var parts = new List<string>
            {
                $"checkout root={CheckoutRoot}"
            };

            if (!string.IsNullOrWhiteSpace(ServerWorkingDirectory))
            {
                parts.Add($"server working directory={ServerWorkingDirectory}");
            }

            if (PrimaryWorkspaceRoot is not null)
            {
                parts.Add($"active root={PrimaryWorkspaceRoot.Name}: {PrimaryWorkspaceRoot.Path}");
            }

            if (Roots.Count > 0)
            {
                parts.Add($"roots={string.Join(", ", Roots.Select(root => root.Name))}");
            }

            if (!string.IsNullOrWhiteSpace(Status))
            {
                parts.Add($"note={Status}");
            }

            return string.Join("; ", parts);
        }
    }

    private sealed record WorkspaceRootSnapshot(
        string Name,
        string Path,
        string Kind,
        bool AllowWrite,
        bool Exists,
        string? SourceRootName);
}
