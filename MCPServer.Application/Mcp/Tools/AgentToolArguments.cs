using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LanguageExt;
using LanguageExt.Common;

namespace MCPServer.Application.Mcp.Tools;

internal static class AgentToolArguments
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
}
