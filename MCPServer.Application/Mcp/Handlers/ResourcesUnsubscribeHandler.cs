using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class ResourcesUnsubscribeHandler : IMcpMethodHandler
{
    private readonly IMcpResourceSubscriptionRegistry _subscriptions;

    public ResourcesUnsubscribeHandler(IMcpResourceSubscriptionRegistry subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        _subscriptions = subscriptions;
    }

    public string Method => McpMethods.ResourcesUnsubscribe;

    public ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var read = ReadRequest(parameters);
        if (read is not { IsSuccess: true, Request: { } request })
        {
            return Fail(read.Error is { } readError ? readError.Message : "resources/unsubscribe parameters are invalid.");
        }

        var outcome = _subscriptions.Unsubscribe(request.Uri).Match(
            Succ: static _ => SubscriptionOutcome.Success(),
            Fail: static error => SubscriptionOutcome.Fail(error));

        if (outcome is not { IsSuccess: true })
        {
            return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(outcome.Error ?? Error.New("resources/unsubscribe failed.")));
        }

        return new ValueTask<Fin<JsonElement>>(Fin.Succ<JsonElement>(McpJsonElements.EmptyObject));
    }

    private static RequestReadResult ReadRequest(JsonElement? parameters)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } suppliedParameters)
        {
            return RequestReadResult.Fail(Error.New("resources/unsubscribe parameters are required and must be an object."));
        }

        ResourceSubscriptionRequest? request;
        try
        {
            request = suppliedParameters.Deserialize(McpJsonSerializerContext.Default.ResourceSubscriptionRequest);
        }
        catch (JsonException ex)
        {
            return RequestReadResult.Fail(Error.New($"resources/unsubscribe parameters are invalid JSON: {ex.Message}"));
        }

        return request is { Uri: { Length: > 0 } }
            ? RequestReadResult.Success(request)
            : RequestReadResult.Fail(Error.New("resources/unsubscribe requires a non-empty uri."));
    }

    private static ValueTask<Fin<JsonElement>> Fail(string message)
    {
        return new ValueTask<Fin<JsonElement>>(Fin.Fail<JsonElement>(Error.New(message)));
    }

    private readonly struct RequestReadResult
    {
        private RequestReadResult(ResourceSubscriptionRequest? request, Error? error, bool isSuccess)
        {
            Request = request;
            Error = error;
            IsSuccess = isSuccess;
        }

        public ResourceSubscriptionRequest? Request { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static RequestReadResult Success(ResourceSubscriptionRequest request)
        {
            return new RequestReadResult(request, default, isSuccess: true);
        }

        public static RequestReadResult Fail(Error error)
        {
            return new RequestReadResult(default, error, isSuccess: false);
        }
    }

    private readonly struct SubscriptionOutcome
    {
        private SubscriptionOutcome(Error? error, bool isSuccess)
        {
            Error = error;
            IsSuccess = isSuccess;
        }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static SubscriptionOutcome Success()
        {
            return new SubscriptionOutcome(default, isSuccess: true);
        }

        public static SubscriptionOutcome Fail(Error error)
        {
            return new SubscriptionOutcome(error, isSuccess: false);
        }
    }
}
