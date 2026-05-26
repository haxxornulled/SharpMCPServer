using System.Text.Json;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class ToolsCallHandler : IMcpMethodHandler
{
    private readonly IMcpToolRegistry _toolRegistry;
    private readonly IMcpToolArgumentValidator _argumentValidator;

    public ToolsCallHandler(IMcpToolRegistry toolRegistry, IMcpToolArgumentValidator argumentValidator)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(argumentValidator);

        _toolRegistry = toolRegistry;
        _argumentValidator = argumentValidator;
    }

    public string Method => McpMethods.ToolsCall;

    public async ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (parameters is not { } suppliedParameters)
        {
            return Fin.Fail<JsonElement>(Error.New("tools/call parameters are required."));
        }

        ToolsCallRequest? request;
        try
        {
            request = suppliedParameters.Deserialize(McpJsonSerializerContext.Default.ToolsCallRequest);
        }
        catch (JsonException ex)
        {
            return Fin.Fail<JsonElement>(Error.New($"tools/call parameters are invalid JSON: {ex.Message}"));
        }

        if (request is not { Name: { } toolName } || string.IsNullOrWhiteSpace(toolName))
        {
            return Fin.Fail<JsonElement>(Error.New("tools/call requires a non-empty tool name."));
        }

        if (request.Arguments is { } suppliedArguments && suppliedArguments is not { ValueKind: JsonValueKind.Object })
        {
            return Fin.Fail<JsonElement>(Error.New("tools/call arguments must be a JSON object when supplied."));
        }

        var toolLookup = _toolRegistry.FindTool(toolName).Match(
            Succ: static tool => ToolLookupResult.Success(tool),
            Fail: static error => ToolLookupResult.Fail(error));

        if (toolLookup is not { IsSuccess: true, Tool: { } tool })
        {
            return Fin.Fail<JsonElement>(toolLookup.Error ?? Error.New("Tool lookup failed."));
        }

        var validation = _argumentValidator.Validate(tool.Descriptor.InputSchema, request.Arguments).Match(
            Succ: static arguments => ToolArgumentValidationResult.Success(arguments),
            Fail: static error => ToolArgumentValidationResult.Fail(error));

        if (validation is not { IsSuccess: true })
        {
            var validationMessage = validation.Error is { } validationError
                ? validationError.Message
                : "Tool argument validation failed.";
            return SerializeToolResult(ToolCallResult.Text(validationMessage, isError: true));
        }

        var execution = (await tool.ExecuteAsync(validation.Arguments, cancellationToken).ConfigureAwait(false)).Match(
            Succ: static result => ToolExecutionResult.Success(result),
            Fail: static error => ToolExecutionResult.Fail(error));

        var result = execution is { IsSuccess: true, Result: { } successfulResult }
            ? successfulResult
            : ToolCallResult.Text(execution.Error is { } executionError ? executionError.Message : "Tool execution failed.", isError: true);

        if (execution is { IsSuccess: true } && !result.IsError && tool.Descriptor.OutputSchema is { } outputSchema)
        {
            var outputValidation = ValidateStructuredOutput(outputSchema, result.StructuredContent).Match(
                Succ: static _ => ToolArgumentValidationResult.Success(default),
                Fail: static error => ToolArgumentValidationResult.Fail(error));

            if (outputValidation is not { IsSuccess: true })
            {
                var outputValidationMessage = outputValidation.Error is { } outputValidationError
                    ? outputValidationError.Message
                    : "Tool structured output validation failed.";
                return SerializeToolResult(ToolCallResult.Text(outputValidationMessage, isError: true));
            }
        }

        return SerializeToolResult(result);
    }

    private Fin<JsonElement> ValidateStructuredOutput(JsonElement outputSchema, JsonElement? structuredContent)
    {
        if (structuredContent is not { } content)
        {
            return Fin.Fail<JsonElement>(Error.New("Tool outputSchema requires structuredContent."));
        }

        return _argumentValidator.ValidateRequiredValue(outputSchema, content, "Tool structuredContent");
    }

    private static Fin<JsonElement> SerializeToolResult(ToolCallResult result)
    {
        return Fin.Succ<JsonElement>(JsonSerializer.SerializeToElement(result, McpJsonSerializerContext.Default.ToolCallResult));
    }

    private readonly struct ToolLookupResult
    {
        private ToolLookupResult(IMcpTool? tool, Error? error, bool isSuccess)
        {
            Tool = tool;
            Error = error;
            IsSuccess = isSuccess;
        }

        public IMcpTool? Tool { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static ToolLookupResult Success(IMcpTool tool)
        {
            return new ToolLookupResult(tool, default, isSuccess: true);
        }

        public static ToolLookupResult Fail(Error error)
        {
            return new ToolLookupResult(default, error, isSuccess: false);
        }
    }

    private readonly struct ToolArgumentValidationResult
    {
        private ToolArgumentValidationResult(JsonElement arguments, Error? error, bool isSuccess)
        {
            Arguments = arguments;
            Error = error;
            IsSuccess = isSuccess;
        }

        public JsonElement Arguments { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static ToolArgumentValidationResult Success(JsonElement arguments)
        {
            return new ToolArgumentValidationResult(arguments, default, isSuccess: true);
        }

        public static ToolArgumentValidationResult Fail(Error error)
        {
            return new ToolArgumentValidationResult(default, error, isSuccess: false);
        }
    }

    private readonly struct ToolExecutionResult
    {
        private ToolExecutionResult(ToolCallResult? result, Error? error, bool isSuccess)
        {
            Result = result;
            Error = error;
            IsSuccess = isSuccess;
        }

        public ToolCallResult? Result { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static ToolExecutionResult Success(ToolCallResult result)
        {
            return new ToolExecutionResult(result, default, isSuccess: true);
        }

        public static ToolExecutionResult Fail(Error error)
        {
            return new ToolExecutionResult(default, error, isSuccess: false);
        }
    }
}
