using System.Text.Json;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class ResourcesTemplatesListHandler : IMcpMethodHandler
{
    private readonly IMcpResourceRegistry _resourceRegistry;

    public ResourcesTemplatesListHandler(IMcpResourceRegistry resourceRegistry)
    {
        ArgumentNullException.ThrowIfNull(resourceRegistry);
        _resourceRegistry = resourceRegistry;
    }

    public string Method => McpMethods.ResourcesTemplatesList;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (parameters is { ValueKind: not JsonValueKind.Object })
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New("resources/templates/list parameters must be an object when supplied.")));
        }

        var outcome = _resourceRegistry.ListResourceTemplates().Match(
            Succ: static result => TemplateListOutcome.Success(result),
            Fail: static error => TemplateListOutcome.Fail(error));

        if (outcome is not { IsSuccess: true, Result: { } result })
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(outcome.Error ?? Error.New("resources/templates/list failed.")));
        }

        var payload = JsonSerializer.SerializeToElement(result, McpJsonSerializerContext.Default.ResourceTemplatesListResult);
        return new ValueTask<Fin<JsonElement>>(Fin.Succ<JsonElement>(payload));
    }

    private readonly struct TemplateListOutcome
    {
        private TemplateListOutcome(ResourceTemplatesListResult? result, Error? error, bool isSuccess)
        {
            Result = result;
            Error = error;
            IsSuccess = isSuccess;
        }

        public ResourceTemplatesListResult? Result { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static TemplateListOutcome Success(ResourceTemplatesListResult result)
        {
            return new TemplateListOutcome(result, default, isSuccess: true);
        }

        public static TemplateListOutcome Fail(Error error)
        {
            return new TemplateListOutcome(default, error, isSuccess: false);
        }
    }
}
