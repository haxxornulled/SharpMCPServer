using System.Globalization;
using System.Text.Json;
using Autofac.Features.Indexed;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp;

public sealed class McpToolRegistry : IMcpToolRegistry
{
    private readonly McpToolDescriptor[] _descriptors;
    private readonly ToolsListResult _allToolsResult;
    private readonly IIndex<string, IMcpTool> _toolsByName;
    private readonly int _pageSize;

    public McpToolRegistry(IEnumerable<IMcpTool> tools, IIndex<string, IMcpTool> toolsByName, McpToolRegistryOptions options, IMcpToolArgumentValidator schemaValidator)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(toolsByName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(schemaValidator);

        var pageSize = Math.Clamp(options.ToolListPageSize, 1, 1_024);
        var toolArray = tools is IMcpTool[] cachedTools ? cachedTools : tools.ToArray();
        var descriptors = new McpToolDescriptor[toolArray.Length];
        var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < toolArray.Length; i++)
        {
            var descriptor = toolArray[i].Descriptor;
            ValidateDescriptor(descriptor, names, schemaValidator);
            descriptors[i] = descriptor;
        }

        _descriptors = descriptors;
        _allToolsResult = new ToolsListResult { Tools = descriptors };
        _toolsByName = toolsByName;
        _pageSize = pageSize;
    }

    public Fin<ToolsListResult> ListTools(string? cursor)
    {
        if (cursor is not { Length: > 0 } && _descriptors.Length <= _pageSize)
        {
            return Fin.Succ<ToolsListResult>(_allToolsResult);
        }

        var start = 0;
        if (cursor is { Length: > 0 } cursorValue &&
            !McpOpaqueCursor.TryReadToolsListCursor(cursorValue, _descriptors.Length, out start))
        {
            return Fin.Fail<ToolsListResult>(Error.New("Invalid tools/list cursor."));
        }

        if (start == _descriptors.Length)
        {
            return Fin.Succ<ToolsListResult>(new ToolsListResult());
        }

        var count = Math.Min(_pageSize, _descriptors.Length - start);
        var page = new McpToolDescriptor[count];
        System.Array.Copy(_descriptors, start, page, 0, count);

        var next = start + count;
        var result = new ToolsListResult
        {
            Tools = page,
            NextCursor = next < _descriptors.Length ? McpOpaqueCursor.CreateToolsListCursor(next, _descriptors.Length) : default
        };

        return Fin.Succ<ToolsListResult>(result);
    }

    public Fin<IMcpTool> FindTool(string name)
    {
        if (!McpToolNameValidator.IsValid(name))
        {
            return Fin.Fail<IMcpTool>(Error.New("Tool name is invalid."));
        }

        return _toolsByName.TryGetValue(name, out var tool)
            ? Fin.Succ<IMcpTool>(tool)
            : Fin.Fail<IMcpTool>(Error.New($"Tool '{name}' was not found."));
    }

    private static void ValidateDescriptor(McpToolDescriptor descriptor, System.Collections.Generic.HashSet<string> names, IMcpToolArgumentValidator schemaValidator)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!McpToolNameValidator.IsValid(descriptor.Name))
        {
            throw new InvalidOperationException($"Tool descriptor name '{descriptor.Name}' does not follow MCP tool-name guidance.");
        }

        if (!names.Add(descriptor.Name))
        {
            throw new InvalidOperationException($"Duplicate MCP tool name '{descriptor.Name}' was registered.");
        }

        ValidateObjectSchema(descriptor.Name, "inputSchema", descriptor.InputSchema, schemaValidator);

        if (descriptor.OutputSchema is { } outputSchema)
        {
            ValidateObjectSchema(descriptor.Name, "outputSchema", outputSchema, schemaValidator);
        }

        if (descriptor.Execution is { TaskSupport: { } taskSupport } && !McpToolTaskSupport.IsValid(taskSupport))
        {
            throw new InvalidOperationException($"Tool '{descriptor.Name}' has invalid execution.taskSupport '{taskSupport}'.");
        }

        if (descriptor.Icons is { Length: > 0 } icons)
        {
            for (var i = 0; i < icons.Length; i++)
            {
                var icon = icons[i];
                if (icon is not { Src: { } source } || !McpIconValidator.IsValidSource(source))
                {
                    throw new InvalidOperationException($"Tool '{descriptor.Name}' icon at index {i.ToString(CultureInfo.InvariantCulture)} must provide an HTTP, HTTPS, or data URI src.");
                }

                if (!McpIconValidator.IsValidMimeType(icon.MimeType))
                {
                    throw new InvalidOperationException($"Tool '{descriptor.Name}' icon at index {i.ToString(CultureInfo.InvariantCulture)} has an unsupported MIME type.");
                }

                if (!McpIconValidator.IsValidTheme(icon.Theme))
                {
                    throw new InvalidOperationException($"Tool '{descriptor.Name}' icon at index {i.ToString(CultureInfo.InvariantCulture)} has an invalid theme.");
                }
            }
        }
    }

    private static void ValidateObjectSchema(string toolName, string propertyName, JsonElement schema, IMcpToolArgumentValidator schemaValidator)
    {
        if (schema is not { ValueKind: JsonValueKind.Object })
        {
            throw new InvalidOperationException($"Tool '{toolName}' must provide a JSON object {propertyName}.");
        }

        if (!schema.TryGetProperty("type"u8, out var typeElement) ||
            typeElement is not { ValueKind: JsonValueKind.String } ||
            !string.Equals(typeElement.GetString(), "object", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Tool '{toolName}' {propertyName} root type must be 'object'.");
        }

        var schemaValidation = schemaValidator.ValidateSchema(schema, $"Tool '{toolName}' {propertyName}").Match(
            Succ: static validSchema => SchemaValidationResult.Success(validSchema),
            Fail: static error => SchemaValidationResult.Fail(error));

        if (schemaValidation is not { IsSuccess: true })
        {
            throw new InvalidOperationException(schemaValidation.Error is { } schemaError
                ? schemaError.Message
                : $"Tool '{toolName}' {propertyName} is not a valid JSON Schema.");
        }
    }

    private readonly struct SchemaValidationResult
    {
        private SchemaValidationResult(JsonElement schema, Error? error, bool isSuccess)
        {
            Schema = schema;
            Error = error;
            IsSuccess = isSuccess;
        }

        public JsonElement Schema { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static SchemaValidationResult Success(JsonElement schema)
        {
            return new SchemaValidationResult(schema, default, isSuccess: true);
        }

        public static SchemaValidationResult Fail(Error error)
        {
            return new SchemaValidationResult(default, error, isSuccess: false);
        }
    }
}
