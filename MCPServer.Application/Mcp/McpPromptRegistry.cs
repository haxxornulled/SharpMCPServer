using System.Globalization;
using Autofac.Features.Indexed;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp;

public sealed class McpPromptRegistry : IMcpPromptRegistry
{
    private readonly McpPromptDescriptor[] _descriptors;
    private readonly PromptsListResult _allPromptsResult;
    private readonly IIndex<string, IMcpPrompt> _promptsByName;
    private readonly int _pageSize;

    public McpPromptRegistry(
        IEnumerable<IMcpPrompt> prompts,
        IIndex<string, IMcpPrompt> promptsByName,
        McpPromptRegistryOptions options)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(promptsByName);
        ArgumentNullException.ThrowIfNull(options);

        var pageSize = Math.Clamp(options.PromptListPageSize, 1, 1_024);
        var promptArray = prompts as IMcpPrompt[] ?? prompts.ToArray();
        var descriptors = new McpPromptDescriptor[promptArray.Length];
        var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < promptArray.Length; i++)
        {
            var descriptor = promptArray[i].Descriptor;
            ValidateDescriptor(descriptor, names);
            descriptors[i] = descriptor;
        }

        _descriptors = descriptors;
        _allPromptsResult = new PromptsListResult { Prompts = descriptors };
        _promptsByName = promptsByName;
        _pageSize = pageSize;
    }

    public Fin<PromptsListResult> ListPrompts(string? cursor)
    {
        if (cursor is not { Length: > 0 } && _descriptors.Length <= _pageSize)
        {
            return Fin.Succ<PromptsListResult>(_allPromptsResult);
        }

        var start = 0;
        if (cursor is { Length: > 0 } cursorValue &&
            !McpOpaqueCursor.TryReadPromptsListCursor(cursorValue, _descriptors.Length, out start))
        {
            return Fin.Fail<PromptsListResult>(Error.New("Invalid prompts/list cursor."));
        }

        if (start == _descriptors.Length)
        {
            return Fin.Succ<PromptsListResult>(new PromptsListResult());
        }

        var count = Math.Min(_pageSize, _descriptors.Length - start);
        var page = new McpPromptDescriptor[count];
        System.Array.Copy(_descriptors, start, page, 0, count);

        var next = start + count;
        var result = new PromptsListResult
        {
            Prompts = page,
            NextCursor = next < _descriptors.Length ? McpOpaqueCursor.CreatePromptsListCursor(next, _descriptors.Length) : default
        };

        return Fin.Succ<PromptsListResult>(result);
    }

    public Fin<IMcpPrompt> FindPrompt(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fin.Fail<IMcpPrompt>(Error.New("Prompt name is required."));
        }

        return _promptsByName.TryGetValue(name, out var prompt)
            ? Fin.Succ<IMcpPrompt>(prompt)
            : Fin.Fail<IMcpPrompt>(Error.New($"Prompt '{name}' was not found."));
    }

    private static void ValidateDescriptor(McpPromptDescriptor descriptor, System.Collections.Generic.HashSet<string> names)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (string.IsNullOrWhiteSpace(descriptor.Name))
        {
            throw new InvalidOperationException("MCP prompt descriptor name is required.");
        }

        if (!names.Add(descriptor.Name))
        {
            throw new InvalidOperationException($"Duplicate MCP prompt name '{descriptor.Name}' was registered.");
        }

        if (descriptor.Arguments is { Length: > 0 } arguments)
        {
            var argumentNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                if (argument is not { Name: { Length: > 0 } argumentName } || string.IsNullOrWhiteSpace(argumentName))
                {
                    throw new InvalidOperationException($"Prompt '{descriptor.Name}' argument at index {i.ToString(CultureInfo.InvariantCulture)} must provide a name.");
                }

                if (!argumentNames.Add(argumentName))
                {
                    throw new InvalidOperationException($"Prompt '{descriptor.Name}' has duplicate argument '{argumentName}'.");
                }
            }
        }

        if (descriptor.Icons is { Length: > 0 } icons)
        {
            for (var i = 0; i < icons.Length; i++)
            {
                var icon = icons[i];
                if (icon is not { Src: { } source } || !McpIconValidator.IsValidSource(source))
                {
                    throw new InvalidOperationException($"Prompt '{descriptor.Name}' icon at index {i.ToString(CultureInfo.InvariantCulture)} must provide an HTTP, HTTPS, or data URI src.");
                }

                if (!McpIconValidator.IsValidMimeType(icon.MimeType))
                {
                    throw new InvalidOperationException($"Prompt '{descriptor.Name}' icon at index {i.ToString(CultureInfo.InvariantCulture)} has an unsupported MIME type.");
                }

                if (!McpIconValidator.IsValidTheme(icon.Theme))
                {
                    throw new InvalidOperationException($"Prompt '{descriptor.Name}' icon at index {i.ToString(CultureInfo.InvariantCulture)} has an invalid theme.");
                }
            }
        }
    }
}
