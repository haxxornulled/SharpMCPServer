using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp;

public sealed class McpCompletionReferenceParser : IMcpCompletionReferenceParser
{
    public Fin<McpCompletionReference> Parse(JsonElement reference)
    {
        if (reference is not { ValueKind: JsonValueKind.Object })
        {
            return Fin.Fail<McpCompletionReference>(Error.New("completion/complete ref must be an object."));
        }

        if (!reference.TryGetProperty("type"u8, out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            return Fin.Fail<McpCompletionReference>(Error.New("completion/complete ref.type is required."));
        }

        var type = typeElement.GetString() ?? string.Empty;
        return type switch
        {
            McpCompletionReferenceTypes.Prompt => ParsePromptReference(reference),
            McpCompletionReferenceTypes.Resource => ParseResourceReference(reference),
            _ => Fin.Fail<McpCompletionReference>(Error.New("completion/complete ref.type is unsupported."))
        };
    }

    private static Fin<McpCompletionReference> ParsePromptReference(JsonElement reference)
    {
        if (!reference.TryGetProperty("name"u8, out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
        {
            return Fin.Fail<McpCompletionReference>(Error.New("completion/complete prompt ref.name is required."));
        }

        var name = nameElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fin.Fail<McpCompletionReference>(Error.New("completion/complete prompt ref.name must not be empty."));
        }

        return Fin.Succ<McpCompletionReference>(McpCompletionReference.Prompt(name));
    }

    private static Fin<McpCompletionReference> ParseResourceReference(JsonElement reference)
    {
        if (!reference.TryGetProperty("uri"u8, out var uriElement) || uriElement.ValueKind != JsonValueKind.String)
        {
            return Fin.Fail<McpCompletionReference>(Error.New("completion/complete resource ref.uri is required."));
        }

        var uri = uriElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(uri))
        {
            return Fin.Fail<McpCompletionReference>(Error.New("completion/complete resource ref.uri must not be empty."));
        }

        return Fin.Succ<McpCompletionReference>(McpCompletionReference.Resource(uri));
    }
}
