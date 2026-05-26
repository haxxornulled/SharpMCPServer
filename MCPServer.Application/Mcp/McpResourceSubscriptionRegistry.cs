using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp;

public sealed class McpResourceSubscriptionRegistry : IMcpResourceSubscriptionRegistry
{
    private readonly IMcpResourceRegistry _resourceRegistry;
    private readonly System.Collections.Generic.HashSet<string> _subscribedUris;
    private readonly object _gate;

    public McpResourceSubscriptionRegistry(IMcpResourceRegistry resourceRegistry)
    {
        ArgumentNullException.ThrowIfNull(resourceRegistry);

        _resourceRegistry = resourceRegistry;
        _subscribedUris = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        _gate = new object();
    }

    public Fin<bool> Subscribe(string uri)
    {
        var validation = ValidateKnownResource(uri);
        if (validation is not { IsSuccess: true })
        {
            return Fin.Fail<bool>(validation.Error ?? Error.New("Resource subscription validation failed."));
        }

        lock (_gate)
        {
            return Fin.Succ<bool>(_subscribedUris.Add(uri));
        }
    }

    public Fin<bool> Unsubscribe(string uri)
    {
        var validation = ValidateKnownResource(uri);
        if (validation is not { IsSuccess: true })
        {
            return Fin.Fail<bool>(validation.Error ?? Error.New("Resource subscription validation failed."));
        }

        lock (_gate)
        {
            _subscribedUris.Remove(uri);
            return Fin.Succ<bool>(true);
        }
    }

    public bool IsSubscribed(string uri)
    {
        if (!McpResourceUriValidator.IsValid(uri))
        {
            return false;
        }

        lock (_gate)
        {
            return _subscribedUris.Contains(uri);
        }
    }

    private ValidationResult ValidateKnownResource(string uri)
    {
        if (!McpResourceUriValidator.IsValid(uri))
        {
            return ValidationResult.Fail(Error.New("Resource uri is invalid."));
        }

        return _resourceRegistry.FindResource(uri).Match(
            Succ: static _ => ValidationResult.Success(),
            Fail: static error => ValidationResult.Fail(error));
    }

    private readonly struct ValidationResult
    {
        private ValidationResult(Error? error, bool isSuccess)
        {
            Error = error;
            IsSuccess = isSuccess;
        }

        public Error? Error { get; }

        public bool IsSuccess { get; }

        public static ValidationResult Success()
        {
            return new ValidationResult(default, isSuccess: true);
        }

        public static ValidationResult Fail(Error error)
        {
            return new ValidationResult(error, isSuccess: false);
        }
    }
}
