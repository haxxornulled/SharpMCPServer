using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class CompletionCompleteHandler : IMcpMethodHandler
{
    private const int MaxCompletionValues = 100;

    private readonly IMcpCompletionReferenceParser _referenceParser;
    private readonly IMcpPromptRegistry _promptRegistry;

    public CompletionCompleteHandler(
        IMcpCompletionReferenceParser referenceParser,
        IMcpPromptRegistry promptRegistry)
    {
        ArgumentNullException.ThrowIfNull(referenceParser);
        ArgumentNullException.ThrowIfNull(promptRegistry);

        _referenceParser = referenceParser;
        _promptRegistry = promptRegistry;
    }

    public string Method => McpMethods.CompletionComplete;

    public async ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (parameters is not { } suppliedParameters)
        {
            return Fin.Fail<JsonElement>(Error.New("completion/complete parameters are required."));
        }

        if (suppliedParameters is not { ValueKind: JsonValueKind.Object })
        {
            return Fin.Fail<JsonElement>(Error.New("completion/complete parameters must be an object."));
        }

        if (!TryValidateArgumentShape(suppliedParameters, out var argumentShapeError))
        {
            return Fin.Fail<JsonElement>(Error.New(argumentShapeError));
        }

        CompleteRequestParams? request;
        try
        {
            request = suppliedParameters.Deserialize(McpJsonSerializerContext.Default.CompleteRequestParams);
        }
        catch (JsonException ex)
        {
            return Fin.Fail<JsonElement>(Error.New("completion/complete parameters are invalid JSON: " + ex.Message));
        }

        if (request is not { Argument: { } argument })
        {
            return Fin.Fail<JsonElement>(Error.New("completion/complete argument is required."));
        }

        if (string.IsNullOrWhiteSpace(argument.Name))
        {
            return Fin.Fail<JsonElement>(Error.New("completion/complete argument.name is required."));
        }

        if (!ValidateCompletionContext(request.Context, out var contextError))
        {
            return Fin.Fail<JsonElement>(Error.New(contextError));
        }

        var reference = _referenceParser.Parse(request.Ref).Match(
            Succ: static value => ReferenceParseOutcome.Success(value),
            Fail: static error => ReferenceParseOutcome.Fail(error));

        if (reference is not { IsSuccess: true })
        {
            return Fin.Fail<JsonElement>(reference.Error ?? Error.New("completion/complete ref is invalid."));
        }

        var result = reference.Reference switch
        {
            { IsPrompt: true, Name: { } promptName } => await CompletePromptAsync(promptName, argument, request.Context, cancellationToken).ConfigureAwait(false),
            { IsResource: true, Uri: { } uri } => CompleteResourceTemplate(uri, argument),
            _ => Fin.Fail<CompleteResult>(Error.New("completion/complete ref is unsupported."))
        };

        var outcome = result.Match(
            Succ: static value => CompleteOutcome.Success(value),
            Fail: static error => CompleteOutcome.Fail(error));

        if (outcome is not { IsSuccess: true, Result: { } completionResult })
        {
            return Fin.Fail<JsonElement>(outcome.Error ?? Error.New("completion/complete failed."));
        }

        if (completionResult.Completion.Values.Length > MaxCompletionValues)
        {
            return Fin.Fail<JsonElement>(Error.New("completion/complete returned more than 100 values."));
        }

        var payload = JsonSerializer.SerializeToElement(completionResult, McpJsonSerializerContext.Default.CompleteResult);
        return Fin.Succ<JsonElement>(payload);
    }

    private static bool TryValidateArgumentShape(JsonElement parameters, out string error)
    {
        if (!parameters.TryGetProperty("argument"u8, out var argument) || argument.ValueKind != JsonValueKind.Object)
        {
            error = "completion/complete argument is required.";
            return false;
        }

        if (!argument.TryGetProperty("name"u8, out var name) || name.ValueKind != JsonValueKind.String)
        {
            error = "completion/complete argument.name is required.";
            return false;
        }

        if (!argument.TryGetProperty("value"u8, out var value) || value.ValueKind != JsonValueKind.String)
        {
            error = "completion/complete argument.value is required.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private async ValueTask<Fin<CompleteResult>> CompletePromptAsync(
        string promptName,
        CompletionArgument argument,
        CompletionContext? context,
        CancellationToken cancellationToken)
    {
        var lookup = _promptRegistry.FindPrompt(promptName).Match(
            Succ: static prompt => PromptLookupOutcome.Success(prompt),
            Fail: static error => PromptLookupOutcome.Fail(error));

        if (lookup is not { IsSuccess: true, Prompt: { } prompt })
        {
            return Fin.Fail<CompleteResult>(lookup.Error ?? Error.New("Prompt lookup failed."));
        }

        if (!PromptHasArgument(prompt.Descriptor, argument.Name))
        {
            return Fin.Fail<CompleteResult>(Error.New("completion/complete argument.name does not match a prompt argument."));
        }

        if (prompt is IMcpPromptCompletionProvider completionProvider)
        {
            return await completionProvider.CompleteAsync(argument, context, cancellationToken).ConfigureAwait(false);
        }

        return Fin.Succ<CompleteResult>(EmptyCompletion());
    }

    private Fin<CompleteResult> CompleteResourceTemplate(string uri, CompletionArgument argument)
    {
        if (!McpResourceUriValidator.IsValid(uri) && !uri.Contains('{', StringComparison.Ordinal))
        {
            return Fin.Fail<CompleteResult>(Error.New("completion/complete resource ref.uri is invalid."));
        }

        if (string.IsNullOrWhiteSpace(argument.Name))
        {
            return Fin.Fail<CompleteResult>(Error.New("completion/complete argument.name is required."));
        }

        // No resource templates are registered in Phase 1 yet. Returning an empty completion set is
        // still a valid completion result and avoids advertising synthetic data.
        return Fin.Succ<CompleteResult>(EmptyCompletion());
    }

    private static bool PromptHasArgument(McpPromptDescriptor descriptor, string argumentName)
    {
        if (descriptor.Arguments is not { Length: > 0 } arguments)
        {
            return false;
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            if (string.Equals(arguments[i].Name, argumentName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ValidateCompletionContext(CompletionContext? context, out string error)
    {
        if (context is not { Arguments: { } arguments })
        {
            error = string.Empty;
            return true;
        }

        if (arguments is not { ValueKind: JsonValueKind.Object })
        {
            error = "completion/complete context.arguments must be an object when supplied.";
            return false;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                error = "completion/complete context.arguments values must be strings.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static CompleteResult EmptyCompletion()
    {
        return new CompleteResult
        {
            Completion = new CompletionResultPayload
            {
                Values = System.Array.Empty<string>(),
                Total = 0,
                HasMore = false
            }
        };
    }

    private readonly struct ReferenceParseOutcome
    {
        private ReferenceParseOutcome(McpCompletionReference reference, Error? error, bool isSuccess)
        {
            Reference = reference;
            Error = error;
            IsSuccess = isSuccess;
        }

        public McpCompletionReference Reference { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static ReferenceParseOutcome Success(McpCompletionReference reference)
        {
            return new ReferenceParseOutcome(reference, default, isSuccess: true);
        }

        public static ReferenceParseOutcome Fail(Error error)
        {
            return new ReferenceParseOutcome(default, error, isSuccess: false);
        }
    }

    private readonly struct PromptLookupOutcome
    {
        private PromptLookupOutcome(IMcpPrompt? prompt, Error? error, bool isSuccess)
        {
            Prompt = prompt;
            Error = error;
            IsSuccess = isSuccess;
        }

        public IMcpPrompt? Prompt { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static PromptLookupOutcome Success(IMcpPrompt prompt)
        {
            return new PromptLookupOutcome(prompt, default, isSuccess: true);
        }

        public static PromptLookupOutcome Fail(Error error)
        {
            return new PromptLookupOutcome(default, error, isSuccess: false);
        }
    }

    private readonly struct CompleteOutcome
    {
        private CompleteOutcome(CompleteResult? result, Error? error, bool isSuccess)
        {
            Result = result;
            Error = error;
            IsSuccess = isSuccess;
        }

        public CompleteResult? Result { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static CompleteOutcome Success(CompleteResult result)
        {
            return new CompleteOutcome(result, default, isSuccess: true);
        }

        public static CompleteOutcome Fail(Error error)
        {
            return new CompleteOutcome(default, error, isSuccess: false);
        }
    }
}
