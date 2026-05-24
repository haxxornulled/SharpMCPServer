using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Prompts;

public sealed class ServerStatusPrompt : IMcpPrompt, IMcpPromptCompletionProvider
{
    private static readonly string[] FocusCompletions =
    [
        "transport compliance",
        "protocol compliance",
        "tools",
        "resources",
        "prompts",
        "completion",
        "logging",
        "performance",
        "security",
        "lifecycle"
    ];

    public McpPromptDescriptor Descriptor { get; } = new McpPromptDescriptor
    {
        Name = McpPromptNames.ServerStatus,
        Title = "Server Status",
        Description = "Creates a concise status prompt for this MCP server.",
        Arguments =
        [
            new McpPromptArgument
            {
                Name = "focus",
                Title = "Focus",
                Description = "Optional status-report focus area.",
                Required = false
            }
        ]
    };

    public ValueTask<Fin<PromptsGetResult>> GetAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var focus = ReadOptionalFocus(arguments);
        if (focus is not { IsSuccess: true })
        {
            return new ValueTask<Fin<PromptsGetResult>>(Fin.Fail<PromptsGetResult>(focus.Error ?? Error.New("Prompt arguments are invalid.")));
        }

        var text = focus.Value is { Length: > 0 } focusValue
            ? "Summarize the MCP server status with emphasis on " + focusValue + "."
            : "Summarize the MCP server status, implemented capabilities, and any relevant operational warnings.";

        var result = new PromptsGetResult
        {
            Description = Descriptor.Description,
            Messages =
            [
                new PromptMessage
                {
                    Role = McpRoles.User,
                    Content = new TextPromptContent
                    {
                        Text = text
                    }
                }
            ]
        };

        return new ValueTask<Fin<PromptsGetResult>>(Fin.Succ<PromptsGetResult>(result));
    }

    public ValueTask<Fin<CompleteResult>> CompleteAsync(
        CompletionArgument argument,
        CompletionContext? context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(argument.Name, "focus", StringComparison.Ordinal))
        {
            return new ValueTask<Fin<CompleteResult>>(Fin.Fail<CompleteResult>(Error.New("server.status completion only supports the 'focus' argument.")));
        }

        var values = FilterValues(argument.Value);
        var result = new CompleteResult
        {
            Completion = new CompletionResultPayload
            {
                Values = values,
                Total = values.Length,
                HasMore = false
            }
        };

        return new ValueTask<Fin<CompleteResult>>(Fin.Succ<CompleteResult>(result));
    }

    private static string[] FilterValues(string prefix)
    {
        if (prefix is not { Length: > 0 })
        {
            return FocusCompletions;
        }

        var matches = new List<string>(FocusCompletions.Length);
        for (var i = 0; i < FocusCompletions.Length; i++)
        {
            var value = FocusCompletions[i];
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                value.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(value);
            }
        }

        return matches.ToArray();
    }

    private static FocusReadResult ReadOptionalFocus(JsonElement? arguments)
    {
        if (arguments is not { } suppliedArguments)
        {
            return FocusReadResult.Success(string.Empty);
        }

        if (suppliedArguments is not { ValueKind: JsonValueKind.Object })
        {
            return FocusReadResult.Fail(Error.New("server.status prompt arguments must be an object."));
        }

        var focus = string.Empty;
        foreach (var property in suppliedArguments.EnumerateObject())
        {
            if (!string.Equals(property.Name, "focus", StringComparison.Ordinal))
            {
                return FocusReadResult.Fail(Error.New("server.status prompt does not accept argument '" + property.Name + "'."));
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return FocusReadResult.Fail(Error.New("server.status prompt argument 'focus' must be a string."));
            }

            focus = property.Value.GetString() ?? string.Empty;
        }

        return FocusReadResult.Success(focus);
    }

    private readonly struct FocusReadResult
    {
        private FocusReadResult(string value, Error? error, bool isSuccess)
        {
            Value = value;
            Error = error;
            IsSuccess = isSuccess;
        }

        public string Value { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static FocusReadResult Success(string value)
        {
            return new FocusReadResult(value, default, isSuccess: true);
        }

        public static FocusReadResult Fail(Error error)
        {
            return new FocusReadResult(string.Empty, error, isSuccess: false);
        }
    }
}
