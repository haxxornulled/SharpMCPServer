using System.Text.Json;
using LanguageExt;
using MCPServer.Client.Interfaces;
using MCPServer.Client.Models;
using MCPServer.Domain.Mcp;

namespace MCPServer.Client.ConsoleApp;

internal sealed class McpClientConsoleDemoSamplingHandler : IMcpClientSamplingHandler
{
    private const string DemoModelName = "mcpserver-client-console-demo";
    private const string DemoStopReason = "endTurn";

    public static McpClientConsoleDemoSamplingHandler Instance { get; } = new McpClientConsoleDemoSamplingHandler();

    private McpClientConsoleDemoSamplingHandler()
    {
    }

    public ValueTask<Fin<McpClientSamplingResponse>> HandleAsync(
        CreateMessageRequestParams parameters,
        IMcpClientTaskRegistry taskRegistry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(taskRegistry);
        cancellationToken.ThrowIfCancellationRequested();

        var prompt = DescribePrompt(parameters);
        var messageCount = parameters.Messages.Length;
        var responseText = $"Demo assistant reply: I received {messageCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} message(s). Prompt: {prompt}";

        var result = new CreateMessageResult
        {
            Model = DemoModelName,
            Role = McpRoles.Assistant,
            StopReason = DemoStopReason,
            Content = CreateTextContent(responseText)
        };

        return ValueTask.FromResult(Fin.Succ(McpClientSamplingResponse.FromResult(result)));
    }

    private static string DescribePrompt(CreateMessageRequestParams parameters)
    {
        if (parameters.Messages.Length == 0)
        {
            return "(no user prompt supplied)";
        }

        var lastMessage = parameters.Messages[^1];
        return lastMessage.Content.ValueKind switch
        {
            JsonValueKind.String => lastMessage.Content.GetString() ?? "(empty prompt)",
            JsonValueKind.Object or JsonValueKind.Array => lastMessage.Content.GetRawText(),
            JsonValueKind.Null => "(null prompt)",
            _ => lastMessage.Content.GetRawText()
        };
    }

    private static JsonElement CreateTextContent(string text)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", text);
            writer.WriteEndObject();
            writer.Flush();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }
}
