using System.Text.Json;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class PromptsGetHandler : IMcpMethodHandler
{
    private readonly IMcpPromptRegistry _promptRegistry;

    public PromptsGetHandler(IMcpPromptRegistry promptRegistry)
    {
        ArgumentNullException.ThrowIfNull(promptRegistry);
        _promptRegistry = promptRegistry;
    }

    public string Method => McpMethods.PromptsGet;

    public async ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (parameters is not { } suppliedParameters)
        {
            return Fin.Fail<JsonElement>(Error.New("prompts/get parameters are required."));
        }

        PromptsGetRequest? request;
        try
        {
            request = suppliedParameters.Deserialize(McpJsonSerializerContext.Default.PromptsGetRequest);
        }
        catch (JsonException ex)
        {
            return Fin.Fail<JsonElement>(Error.New($"prompts/get parameters are invalid JSON: {ex.Message}"));
        }

        if (request is not { Name: { } promptName } || string.IsNullOrWhiteSpace(promptName))
        {
            return Fin.Fail<JsonElement>(Error.New("prompts/get requires a non-empty name."));
        }

        if (request.Arguments is { } suppliedArguments && suppliedArguments is not { ValueKind: JsonValueKind.Object })
        {
            return Fin.Fail<JsonElement>(Error.New("prompts/get arguments must be an object when supplied."));
        }

        if (request.Arguments is { ValueKind: JsonValueKind.Object } argumentObject && !AllArgumentValuesAreStrings(argumentObject))
        {
            return Fin.Fail<JsonElement>(Error.New("prompts/get arguments must be an object whose values are strings."));
        }

        var lookup = _promptRegistry.FindPrompt(promptName).Match(
            Succ: static prompt => PromptLookupResult.Success(prompt),
            Fail: static error => PromptLookupResult.Fail(error));

        if (lookup is not { IsSuccess: true, Prompt: { } prompt })
        {
            return Fin.Fail<JsonElement>(lookup.Error ?? Error.New("Prompt lookup failed."));
        }

        var promptResult = (await prompt.GetAsync(request.Arguments, cancellationToken).ConfigureAwait(false)).Match(
            Succ: static result => PromptGetOutcome.Success(result),
            Fail: static error => PromptGetOutcome.Fail(error));

        if (promptResult is not { IsSuccess: true, Result: { } result })
        {
            return Fin.Fail<JsonElement>(promptResult.Error ?? Error.New("Prompt get failed."));
        }

        if (result.Messages.Length == 0)
        {
            return Fin.Fail<JsonElement>(Error.New("prompts/get result must include at least one message."));
        }

        for (var i = 0; i < result.Messages.Length; i++)
        {
            var message = result.Messages[i];
            if (!McpRoles.IsValid(message.Role))
            {
                return Fin.Fail<JsonElement>(Error.New("prompts/get result message role must be 'user' or 'assistant'."));
            }
        }

        var payload = JsonSerializer.SerializeToElement(result, McpJsonSerializerContext.Default.PromptsGetResult);
        return Fin.Succ<JsonElement>(payload);
    }

    private static bool AllArgumentValuesAreStrings(JsonElement arguments)
    {
        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return false;
            }
        }

        return true;
    }

    private readonly struct PromptLookupResult
    {
        private PromptLookupResult(IMcpPrompt? prompt, Error? error, bool isSuccess)
        {
            Prompt = prompt;
            Error = error;
            IsSuccess = isSuccess;
        }

        public IMcpPrompt? Prompt { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static PromptLookupResult Success(IMcpPrompt prompt)
        {
            return new PromptLookupResult(prompt, default, isSuccess: true);
        }

        public static PromptLookupResult Fail(Error error)
        {
            return new PromptLookupResult(default, error, isSuccess: false);
        }
    }

    private readonly struct PromptGetOutcome
    {
        private PromptGetOutcome(PromptsGetResult? result, Error? error, bool isSuccess)
        {
            Result = result;
            Error = error;
            IsSuccess = isSuccess;
        }

        public PromptsGetResult? Result { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static PromptGetOutcome Success(PromptsGetResult result)
        {
            return new PromptGetOutcome(result, default, isSuccess: true);
        }

        public static PromptGetOutcome Fail(Error error)
        {
            return new PromptGetOutcome(default, error, isSuccess: false);
        }
    }
}
