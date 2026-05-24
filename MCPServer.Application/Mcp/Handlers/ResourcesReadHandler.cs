using System.Text.Json;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp.Handlers;

public sealed class ResourcesReadHandler : IMcpMethodHandler
{
    private readonly IMcpResourceRegistry _resourceRegistry;

    public ResourcesReadHandler(IMcpResourceRegistry resourceRegistry)
    {
        ArgumentNullException.ThrowIfNull(resourceRegistry);
        _resourceRegistry = resourceRegistry;
    }

    public string Method => McpMethods.ResourcesRead;

    public async ValueTask<Fin<JsonElement>> HandleAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (parameters is not { } suppliedParameters)
        {
            return Fin.Fail<JsonElement>(Error.New("resources/read parameters are required."));
        }

        ResourcesReadRequest? request;
        try
        {
            request = suppliedParameters.Deserialize(McpJsonSerializerContext.Default.ResourcesReadRequest);
        }
        catch (JsonException ex)
        {
            return Fin.Fail<JsonElement>(Error.New($"resources/read parameters are invalid JSON: {ex.Message}"));
        }

        if (request is not { Uri: { } uri } || string.IsNullOrWhiteSpace(uri))
        {
            return Fin.Fail<JsonElement>(Error.New("resources/read requires a non-empty uri."));
        }

        var lookup = _resourceRegistry.FindResource(uri).Match(
            Succ: static resource => ResourceLookupResult.Success(resource),
            Fail: static error => ResourceLookupResult.Fail(error));

        if (lookup is not { IsSuccess: true, Resource: { } resource })
        {
            return Fin.Fail<JsonElement>(lookup.Error ?? Error.New("Resource lookup failed."));
        }

        var read = (await resource.ReadAsync(cancellationToken).ConfigureAwait(false)).Match(
            Succ: static result => ResourceReadOutcome.Success(result),
            Fail: static error => ResourceReadOutcome.Fail(error));

        if (read is not { IsSuccess: true, Result: { } result })
        {
            return Fin.Fail<JsonElement>(read.Error ?? Error.New("Resource read failed."));
        }

        var payload = JsonSerializer.SerializeToElement(result, McpJsonSerializerContext.Default.ResourcesReadResult);
        return Fin.Succ<JsonElement>(payload);
    }

    private readonly struct ResourceLookupResult
    {
        private ResourceLookupResult(IMcpResource? resource, Error? error, bool isSuccess)
        {
            Resource = resource;
            Error = error;
            IsSuccess = isSuccess;
        }

        public IMcpResource? Resource { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static ResourceLookupResult Success(IMcpResource resource)
        {
            return new ResourceLookupResult(resource, default, isSuccess: true);
        }

        public static ResourceLookupResult Fail(Error error)
        {
            return new ResourceLookupResult(default, error, isSuccess: false);
        }
    }

    private readonly struct ResourceReadOutcome
    {
        private ResourceReadOutcome(ResourcesReadResult? result, Error? error, bool isSuccess)
        {
            Result = result;
            Error = error;
            IsSuccess = isSuccess;
        }

        public ResourcesReadResult? Result { get; }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static ResourceReadOutcome Success(ResourcesReadResult result)
        {
            return new ResourceReadOutcome(result, default, isSuccess: true);
        }

        public static ResourceReadOutcome Fail(Error error)
        {
            return new ResourceReadOutcome(default, error, isSuccess: false);
        }
    }
}
