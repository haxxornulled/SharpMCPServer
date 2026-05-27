using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LanguageExt;
using LanguageExt.Common;

namespace MCPServer.Tools.Workspace;

internal static class WorkspaceToolArguments
{
    public static Fin<TRequest> Parse<TRequest>(JsonElement? arguments, JsonTypeInfo<TRequest> typeInfo, string toolName)
        where TRequest : class
    {
        if (arguments is not { ValueKind: JsonValueKind.Object })
        {
            return Fin.Fail<TRequest>(Error.New($"{toolName} expects an arguments object."));
        }

        var request = JsonSerializer.Deserialize(arguments.Value.GetRawText(), typeInfo);
        return request is null
            ? Fin.Fail<TRequest>(Error.New($"{toolName} arguments could not be parsed."))
            : Fin.Succ(request);
    }

    public static Fin<Unit> RequireString(string? value, string propertyName, string toolName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Fin.Fail<Unit>(Error.New($"{toolName} requires a string {propertyName}."))
            : Fin.Succ(Unit.Default);
    }

    public static Fin<Unit> RequireObjectArgument(JsonElement? arguments, string toolName)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } suppliedArguments)
        {
            return Fin.Fail<Unit>(Error.New($"{toolName} expects an arguments object."));
        }

        using var properties = suppliedArguments.EnumerateObject();
        return properties.MoveNext()
            ? Fin.Succ(Unit.Default)
            : Fin.Fail<Unit>(Error.New($"{toolName} expects at least one argument."));
    }

    public static Fin<Unit> RequireNoArguments(JsonElement? arguments, string toolName)
    {
        if (arguments is null)
        {
            return Fin.Succ(Unit.Default);
        }

        if (arguments is not { ValueKind: JsonValueKind.Object } suppliedArguments)
        {
            return Fin.Fail<Unit>(Error.New($"{toolName} expects an arguments object."));
        }

        using var properties = suppliedArguments.EnumerateObject();
        return properties.MoveNext()
            ? Fin.Fail<Unit>(Error.New($"{toolName} does not accept arguments."))
            : Fin.Succ(Unit.Default);
    }
}
